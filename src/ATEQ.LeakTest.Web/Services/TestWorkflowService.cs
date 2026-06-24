using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Models;

namespace ATEQ.LeakTest.Web.Services;

public class TestWorkflowService
{
    private const int ArmedContextStaleMs = 8000;
    public const int DefaultMonitorTimeoutMs = 30 * 60 * 1000;
    public const int MaxMonitorSampleCount = 10000;
    public const int ActiveSampleWindowCount = 10000;
    public const int SavedSampleWindowCount = 10000;
    private static readonly HashSet<int> AteqIdleStepCodes = [0, 65535];

    private readonly IServiceScopeFactory _scopeFactory;
    private ActiveRun? _activeRun;
    private TestContext? _pendingContext;
    private TestContext? _selectedContext;
    private int? _lastObservedStepCode;
    private long _lastRejectedObservedRunAt;
    private volatile bool _commandInFlight;
    private volatile bool _observeInFlight;

    public TestWorkflowService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public ActiveTestState GetActiveState()
    {
        if (_activeRun != null)
            return DeepClone(_activeRun.State);

        if (_pendingContext != null)
        {
            return new ActiveTestState
            {
                Running = false,
                Stage = _pendingContext.Armed ? "armed" : "ready",
                Message = _pendingContext.Armed ? "Waiting for ATEQ step 2" : "Ready to start",
                StartMode = _pendingContext.StartMode,
                QrCode = _pendingContext.QrCode,
                ScannerEventId = _pendingContext.ScannerEventId,
                OperatorName = _pendingContext.Operator?.Name ?? "",
                MatchedProduct = ToMatchedProduct(_pendingContext.ProductProfile),
                ResultCode = "UNKNOWN"
            };
        }

        if (_selectedContext != null)
        {
            return new ActiveTestState
            {
                Running = false,
                Stage = "ready",
                Message = "Ready to start",
                StartMode = _selectedContext.StartMode,
                OperatorName = _selectedContext.Operator?.Name ?? "",
                MatchedProduct = ToMatchedProduct(_selectedContext.ProductProfile),
                ResultCode = "UNKNOWN"
            };
        }

        return new ActiveTestState { Running = false, Stage = "idle", Message = "No active test" };
    }

    public bool ShouldObserveTelemetry()
        => !_commandInFlight && !(_activeRun?.State.Running == true);

    // ==================== Auto-start from scan ====================

    public async Task<object?> MaybeAutoStartFromScanAsync(ScannerEvent scanEvent)
    {
        if ((_activeRun?.State.Running == true) || _commandInFlight || HasArmedPendingContext())
            return null;

        var scanBinding = await ResolveScanBindingAsync(scanEvent.RawText, null);
        if (string.IsNullOrEmpty(scanBinding.qrCode)) return null;

        // For scan-triggered auto-start, resolve product from the QR code first.
        // Context (pending/selected) may point to a different product that the
        // operator chose manually 鈥?the QR should take priority for auto-start.
        var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        var plc = scope.ServiceProvider.GetRequiredService<PlcService>();
        var productProfile = await db.MatchProductProfileByQrAsync(scanBinding.qrCode)
                          ?? _pendingContext?.ProductProfile
                          ?? _selectedContext?.ProductProfile;
        if (productProfile == null) return null;

        // Notify PLC that scan matched a product
        _ = plc.WriteM0Async(true);

        if (!productProfile.ScanAutoStartEnabled) return null;

        if (productProfile.ScanMatchEnabled)
        {
            try { AssertManualProductMatchesScan(productProfile, scanBinding.qrCode); }
            catch { return null; }
        }

        try
        {
            return await StartAsync(new StartPayload
            {
                QrCode = scanBinding.qrCode,
                ScannerEventId = scanBinding.scannerEventId,
                ProductModel = productProfile.ProductModel,
                OperatorName = _selectedContext?.Operator?.Name,
                StartMode = "scan"
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[workflow] auto start failed: {ex.Message}");
            return null;
        }
    }

    // ==================== Sync context ====================

    public async Task<object> SyncContextAsync(ContextPayload payload)
    {
        if (_activeRun?.State.Running == true)
            throw new TestWorkflowException("Cannot change context during active test", 409);

        await ReleaseStaleArmedContextIfSafeAsync();

        if (HasArmedPendingContext() || _commandInFlight)
            throw new TestWorkflowException("Cannot change context while waiting for step 2", 409);

        var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        var modbus = scope.ServiceProvider.GetRequiredService<ModbusService>();
        var plc = scope.ServiceProvider.GetRequiredService<PlcService>();

        var context = await BuildContextAsync(db, new StartPayload
        {
            ProductModel = payload.ProductModel,
            OperatorName = payload.OperatorName,
            StartMode = "manual"
        }, false);
        _selectedContext = CreateSelectedContext(context);
        _pendingContext = CreatePendingContext(context, false);

        // M0: ON for scan-free products (PLC "ready, no scan needed"), OFF otherwise
        await ApplyScanFreeM0Async(plc, context.ProductProfile);

        int? currentProgram = null;
        await modbus.SelectProgramAsync(context.ProductProfile.AteqProgramNo);
        try
        {
            var status = await modbus.ReadRealtimeStatusAsync();
            currentProgram = status.CurrentProgram;
        }
        catch
        {
            currentProgram = context.ProductProfile.AteqProgramNo;
        }

        return new
        {
            success = true,
            message = "Test context synced and program selected",
            selectedProgram = context.ProductProfile.AteqProgramNo,
            currentProgram,
            context = GetActiveState()
        };
    }

    // ==================== M0 scan-free helpers ====================

    private static bool IsScanFreeProduct(ProductProfile product) =>
        !product.ScanConfirmEnabled && !product.ScanMatchEnabled && !product.ScanAutoStartEnabled;

    /// <summary>Public check for coordinator: is the currently selected product scan-free?</summary>
    public bool IsSelectedProductScanFree() =>
        _selectedContext?.ProductProfile is { } p && IsScanFreeProduct(p);

    /// <summary>Write M0=ON if scan-free, M0=OFF if not. Fire-and-forget.</summary>
    private static async Task ApplyScanFreeM0Async(PlcService plc, ProductProfile product)
    {
        if (IsScanFreeProduct(product))
            await plc.WriteM0Async(true);
        else
            await plc.WriteM0Async(false);
    }

    /// <summary>
    /// Restore M0=ON after test completion or reset, if the current
    /// selected product is still scan-free. Called by coordinator.
    /// </summary>
    public async Task RestoreScanFreeM0IfNeededAsync()
    {
        if (_selectedContext?.ProductProfile is { } p && IsScanFreeProduct(p))
        {
            var scope = CreateScope();
            var plc = scope.ServiceProvider.GetRequiredService<PlcService>();
            await plc.WriteM0Async(true);
            Console.WriteLine($"[workflow] M0 restored (scan-free product: {p.ProductModel})");
        }
    }

    // ==================== Start ====================

    public async Task<object> StartAsync(StartPayload payload)
    {
        if (_activeRun?.State.Running == true)
            throw new TestWorkflowException("A test is already running", 409);

        await ReleaseStaleArmedContextIfSafeAsync();

        if (_commandInFlight || HasArmedPendingContext())
            throw new TestWorkflowException("Start command already sent, waiting for step 2", 409);

        var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        var modbus = scope.ServiceProvider.GetRequiredService<ModbusService>();

        TestContext context;
        try
        {
            context = await BuildContextAsync(db, payload, true);
        }
        catch (TestWorkflowException ex) when (ex.Message.Contains("QR code does not contain the keyword"))
        {
            try { await modbus.ResetDeviceAsync(); }
            catch (Exception resetEx) { Console.Error.WriteLine($"[workflow] reset after scan mismatch failed: {resetEx.Message}"); }
            throw;
        }

        _pendingContext = CreatePendingContext(context, true);
        _selectedContext = CreateSelectedContext(context);
        _activeRun = null;
        _commandInFlight = true;
        try
        {
            if (payload.SkipProgramSelect != true)
                await modbus.SelectProgramAsync(context.ProductProfile.AteqProgramNo);
            await modbus.StartTestAsync();
        }
        catch
        {
            _pendingContext = CreatePendingContext(context, false);
            throw;
        }
        finally { _commandInFlight = false; }

        return new
        {
            success = true,
            message = "Start command sent, waiting for step 2",
            resultCode = "UNKNOWN",
            errorCode = (string?)null
        };
    }

    // ==================== Observe telemetry ====================

    public async Task<object?> ObserveTelemetryAsync(RealtimeStatus telemetry)
    {
        var previousStepCode = _lastObservedStepCode;
        _lastObservedStepCode = telemetry?.StepCode;

        if (telemetry == null || !ShouldObserveTelemetry() || _observeInFlight)
            return null;

        var stepCode = telemetry.StepCode;
        var enteredStep2 = stepCode == 2 && previousStepCode != 2;
        var recoveredActiveStep = !enteredStep2 && _activeRun == null && IsAteqActiveStep(stepCode);

        if (!enteredStep2 && !recoveredActiveStep) return null;

        _observeInFlight = true;
        try
        {
            TestContext context;
            try { context = await ResolveObservedContextAsync(telemetry); }
            catch (TestWorkflowException ex) when (ex.StatusCode == 409)
            {
                await StopRejectedObservedRunAsync(telemetry, ex.Message);
                return null;
            }

            if (context == null)
            {
                Console.WriteLine($"[workflow] active step {stepCode} detected without context, program={telemetry.CurrentProgram}");
                return null;
            }

            if (recoveredActiveStep)
                Console.WriteLine($"[workflow] recovered active test at step {stepCode}");

            return await BeginObservedRunAsync(context, telemetry);
        }
        finally { _observeInFlight = false; }
    }

    private async Task StopRejectedObservedRunAsync(RealtimeStatus telemetry, string reason)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastRejectedObservedRunAt < 2000) return;
        _lastRejectedObservedRunAt = now;

        Console.WriteLine($"[workflow] stopping physical start: {reason}, program={telemetry.CurrentProgram}, step={telemetry.StepCode}");
        var scope = CreateScope();
        var modbus = scope.ServiceProvider.GetRequiredService<ModbusService>();
        try { await modbus.ResetDeviceAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[workflow] failed to stop rejected physical start: {ex.Message}"); }
    }

    private Task<object?> BeginObservedRunAsync(TestContext context, RealtimeStatus initialTelemetry)
    {
        if (_activeRun?.State.Running == true) return Task.FromResult<object?>(null);

        var startedAt = DateTime.UtcNow.ToString("o");
        _pendingContext = null;
        var state = new ActiveTestState
        {
            Running = true,
            Stage = "monitoring",
            Message = "Monitoring stepcode and telemetry",
            StartedAt = startedAt,
            StartMode = context.StartMode,
            QrCode = context.RecordQrCode ?? context.QrCode,
            ScannerEventId = context.ScannerEventId,
            OperatorName = context.Operator?.Name ?? "",
            MatchedProduct = ToMatchedProduct(context.ProductProfile),
            ResultCode = "UNKNOWN"
        };

        _activeRun = new ActiveRun
        {
            State = state,
            CancelRequested = false
        };

        _ = MonitorRunAsync(context.ProductProfile, context.Operator, context.QrCode ?? "",
            context.RecordQrCode ?? "", context.ScannerEventId, context.StartMode, initialTelemetry);

        return Task.FromResult<object?>(new { success = true, message = "Test monitoring started" });
    }

    // ==================== Monitor loop ====================

    private async Task MonitorRunAsync(ProductProfile productProfile, Models.Operator? op,
        string qrCode, string recordQrCode, string? scannerEventId, string startMode, RealtimeStatus initialTelemetry)
    {
        var state = _activeRun!.State;
        var samples = new List<TelemetrySample>();
        var step6Samples = new List<TelemetrySample>();
        RealtimeStatus? lastTelemetry = null;
        var testStarted = false;
        double? testPressure = null, finalPressure = null, finalLeak = null;
        string? finalPressureUnit = null, finalLeakUnit = null;
        var finalResultCode = "UNKNOWN";
        string? finalErrorCode = null;
        int? rawStatusWord = null;
        int? previousStepCode = null;
        var startedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var scope = CreateScope();
        var modbus = scope.ServiceProvider.GetRequiredService<ModbusService>();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();

        var ateqConfig = await db.GetCommConfigAsync("ateq");
        var pollIntervalMs = Math.Max(50, ateqConfig?.PollIntervalMs > 0 ? ateqConfig.PollIntervalMs : 100);

        bool ApplyTelemetry(RealtimeStatus telemetry, string sampledAt, long elapsedMs)
        {
            var sample = new TelemetrySample
            {
                SampledAt = sampledAt,
                ElapsedMs = elapsedMs,
                StepCode = telemetry.StepCode,
                Pressure = telemetry.Pressure,
                PressureUnit = telemetry.PressureUnit,
                Leak = telemetry.Leak,
                LeakUnit = telemetry.LeakUnit,
                ResultCode = telemetry.ResultCode,
                ErrorCode = telemetry.ErrorCode,
                StatusWord = telemetry.StatusWord
            };

            samples.Add(sample);
            if (samples.Count > MaxMonitorSampleCount) samples.RemoveAt(0);

            state.Samples = samples.Skip(Math.Max(0, samples.Count - ActiveSampleWindowCount)).ToList();
            state.LatestTelemetry = sample;
            state.Message = $"Monitoring step {telemetry.StepCode}";

            rawStatusWord = telemetry.StatusWord;
            lastTelemetry = telemetry;

            if (telemetry.StepCode >= 2 && telemetry.StepCode <= 100)
                testStarted = true;

            if (telemetry.StepCode == 5)
                testPressure = telemetry.Pressure;

            if (telemetry.StepCode == 6)
            {
                step6Samples.Add(sample);
                while (step6Samples.Count > 0 && sample.ElapsedMs - step6Samples[0].ElapsedMs > 1000)
                    step6Samples.RemoveAt(0);
                finalPressureUnit = sample.PressureUnit ?? finalPressureUnit;
            }

            if (previousStepCode == 6 && telemetry.StepCode != 6 && step6Samples.Count > 0)
            {
                finalPressure = step6Samples[^1].Pressure;
                finalPressureUnit = step6Samples[^1].PressureUnit ?? finalPressureUnit;
            }

            if (testStarted && telemetry.StepCode == 65535)
            {
                finalLeak = telemetry.Leak;
                finalLeakUnit = telemetry.LeakUnit ?? finalLeakUnit;
                if (step6Samples.Count > 0)
                {
                    finalPressure = step6Samples[^1].Pressure;
                    finalPressureUnit = step6Samples[^1].PressureUnit ?? finalPressureUnit;
                }
                finalResultCode = telemetry.ResultCode;
                finalErrorCode = telemetry.ErrorCode;
                previousStepCode = telemetry.StepCode;
                return true;
            }

            if (testStarted && telemetry.StepCode == 0 && previousStepCode != null)
            {
                if (step6Samples.Count > 0)
                {
                    finalPressure = step6Samples[^1].Pressure;
                    finalPressureUnit = step6Samples[^1].PressureUnit ?? finalPressureUnit;
                }
                finalLeak ??= telemetry.Leak;
                finalLeakUnit ??= telemetry.LeakUnit;
                finalResultCode = telemetry.ResultCode;
                finalErrorCode = telemetry.ErrorCode ?? "ATEQ_RETURNED_IDLE";
                previousStepCode = telemetry.StepCode;
                return true;
            }

            previousStepCode = telemetry.StepCode;
            return false;
        }

        try
        {
            if (initialTelemetry != null)
            {
                var completed = ApplyTelemetry(initialTelemetry, DateTime.UtcNow.ToString("o"), 0);
                if (completed) state.Message = "Monitoring completed on initial step";
            }

            while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAtMs < DefaultMonitorTimeoutMs)
            {
                if (_activeRun == null || _activeRun.CancelRequested)
                    throw new TestWorkflowException("Test aborted by reset", 409);

                if (lastTelemetry != null && (lastTelemetry.StepCode == 65535 || lastTelemetry.StepCode == 0) && testStarted)
                    break;

                await Task.Delay(pollIntervalMs);

                if (_activeRun == null || _activeRun.CancelRequested)
                    throw new TestWorkflowException("Test aborted by reset", 409);

                var telemetry = await modbus.ReadRealtimeStatusAsync();
                var sampledAt = DateTime.UtcNow.ToString("o");
                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAtMs;
                if (ApplyTelemetry(telemetry, sampledAt, elapsedMs)) break;
            }

            if (!testStarted)
                throw new TestWorkflowException("ATEQ test did not enter execution steps", 504);

            if (lastTelemetry == null)
                throw new TestWorkflowException("ATEQ returned no telemetry", 504);

            if (finalPressure == null && step6Samples.Count > 0)
            {
                finalPressure = step6Samples[^1].Pressure;
                finalPressureUnit = step6Samples[^1].PressureUnit ?? finalPressureUnit;
            }

            finalLeak ??= lastTelemetry.Leak;
            finalLeakUnit ??= lastTelemetry.LeakUnit;

            var savedRecord = await db.SaveTestRecordAsync(new TestRecord
            {
                StartedAt = state.StartedAt ?? DateTime.UtcNow.ToString("o"),
                FinishedAt = DateTime.UtcNow.ToString("o"),
                StartMode = startMode,
                QrCode = recordQrCode,
                ProductId = productProfile.Id,
                ProductModel = productProfile.ProductModel,
                AteqProgramNo = productProfile.AteqProgramNo,
                OperatorName = op?.Name ?? "",
                TestPressure = testPressure,
                FinalPressure = finalPressure,
                PressureUnit = finalPressureUnit ?? lastTelemetry.PressureUnit,
                FinalLeak = finalLeak,
                LeakUnit = finalLeakUnit ?? lastTelemetry.LeakUnit,
                ResultCode = finalResultCode,
                ErrorCode = finalErrorCode,
                RawStatusWord = rawStatusWord,
                SampleCount = samples.Count,
                Samples = System.Text.Json.JsonSerializer.Serialize(
                    samples.Skip(Math.Max(0, samples.Count - SavedSampleWindowCount)))
            });

            state.Running = false;
            state.Stage = "completed";
            state.Message = "Test completed";
            state.FinishedAt = savedRecord.FinishedAt;
            state.ResultCode = savedRecord.ResultCode;
            state.ErrorCode = savedRecord.ErrorCode;
            state.SavedRecord = new SavedRecordInfo
            {
                Id = savedRecord.Id,
                SequenceCode = savedRecord.SequenceCode,
                QrCode = savedRecord.QrCode,
                ProductModel = savedRecord.ProductModel,
                ResultCode = savedRecord.ResultCode,
                ErrorCode = savedRecord.ErrorCode,
                ErrorText = ModbusProtocol.DeriveErrorText(savedRecord.RawStatusWord, savedRecord.ErrorCode, savedRecord.ResultCode),
                FinalPressure = savedRecord.FinalPressure,
                PressureUnit = savedRecord.PressureUnit,
                FinalLeak = savedRecord.FinalLeak,
                LeakUnit = savedRecord.LeakUnit
            };

            // Clean up scanner
            if (!string.IsNullOrEmpty(qrCode) || !string.IsNullOrEmpty(scannerEventId))
            {
                var scannerScope = CreateScope();
                var scanner = scannerScope.ServiceProvider.GetRequiredService<ScannerService>();
                try
                {
                    if (!string.IsNullOrEmpty(scannerEventId))
                        await db.DeleteScannerEventByIdAsync(scannerEventId);
                    scanner.ConsumeCurrentScan(new { ScannerEventId = scannerEventId, QrCode = qrCode });
                    state.QrCode = "";
                    state.ScannerEventId = null;
                }
                catch (Exception ex) { Console.Error.WriteLine($"[workflow] failed to clear scanner: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            if (_activeRun is { CancelRequested: true })
            {
                state.Running = false;
                state.Stage = "aborted";
                state.Message = "ATEQ reset requested";
                state.FinishedAt = DateTime.UtcNow.ToString("o");
                state.ResultCode = "UNKNOWN";
                state.ErrorCode = "ATEQ_RESET_ABORT";
                state.SavedRecord = null;
                return;
            }

            var message = ex is TestWorkflowException or ModbusException ? ex.Message : "Test workflow failed";
            var statusCode = ex is TestWorkflowException twe ? twe.StatusCode : 503;

            state.Running = false;
            state.Stage = "failed";
            state.Message = message;
            state.FinishedAt = DateTime.UtcNow.ToString("o");
            state.ResultCode = "UNKNOWN";
            state.ErrorCode = message;
        }
        finally
        {
            var activeRun = _activeRun;
            _ = Task.Run(async () =>
            {
                await Task.Delay(15000);
                if (activeRun == _activeRun && !(activeRun?.State.Running == true))
                    _activeRun = null;
            });
        }
    }

    // ==================== PLC-triggered start ====================

    /// <summary>
    /// Entry point for PLC M1 rising edge. Uses the already-selected
    /// product + operator context (set via SyncContextAsync).
    /// Returns null if no context is available 鈥?callers must handle gracefully.
    /// All existing scan/validation rules from BuildContextAsync still apply.
    /// </summary>
    public async Task<object?> StartFromSelectedContextAsync(string startMode)
    {
        if (_activeRun?.State.Running == true) return null;
        if (_commandInFlight || HasArmedPendingContext()) return null;

        await ReleaseStaleArmedContextIfSafeAsync();

        if (_selectedContext == null) return null;

        var payload = new StartPayload
        {
            ProductModel = _selectedContext.ProductProfile.ProductModel,
            OperatorName = _selectedContext.Operator?.Name,
            QrCode = _selectedContext.QrCode,
            ScannerEventId = _selectedContext.ScannerEventId,
            StartMode = startMode
        };

        return await StartAsync(payload);
    }

    // ==================== Reset ====================

    public void HandleResetCommand()
    {
        _pendingContext = null;
        if (_activeRun == null) return;
        if (_activeRun.State.Running)
        {
            _activeRun.CancelRequested = true;
            return;
        }
        _activeRun = null;
    }

    // ==================== Context helpers ====================

    private async Task<TestContext> BuildContextAsync(DatabaseService db, StartPayload payload, bool allowQrResolution)
    {
        var productProfile = await ResolveProductAsync(db, payload);
        var op = await ResolveOperatorAsync(db, payload.OperatorName);
        var scanBinding = allowQrResolution ? await ResolveScanBindingAsync(payload.QrCode, payload.ScannerEventId) : (qrCode: "", scannerEventId: (string?)null);
        var scanConfirmEnabled = productProfile.ScanConfirmEnabled;
        var scanMatchEnabled = productProfile.ScanMatchEnabled;
        var requestedStartMode = (payload.StartMode ?? "manual").Trim().ToLowerInvariant();
        var startMode = !string.IsNullOrEmpty(scanBinding.qrCode)
            ? "scan"
            : requestedStartMode is "plc" ? "plc" : "manual";

        if (!string.IsNullOrEmpty(payload.ProductModel) && scanConfirmEnabled)
            AssertManualProductHasScan(productProfile, scanBinding.qrCode);

        if (!string.IsNullOrEmpty(payload.ProductModel) && scanMatchEnabled)
            AssertManualProductMatchesScan(productProfile, scanBinding.qrCode);

        return new TestContext
        {
            ProductProfile = productProfile,
            Operator = op,
            QrCode = scanBinding.qrCode,
            RecordQrCode = scanBinding.qrCode,
            ScannerEventId = scanBinding.scannerEventId,
            StartMode = startMode
        };
    }

    private async Task<TestContext> ResolveObservedContextAsync(RealtimeStatus telemetry)
    {
        var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        var scanner = scope.ServiceProvider.GetRequiredService<ScannerService>();

        ProductProfile? productProfile = null;
        var op = _pendingContext?.Operator ?? _selectedContext?.Operator;
        var startMode = _pendingContext?.StartMode ?? "manual";

        if (_pendingContext?.ProductProfile != null &&
            _pendingContext.ProductProfile.AteqProgramNo == telemetry.CurrentProgram)
            productProfile = _pendingContext.ProductProfile;

        productProfile ??= await db.GetProductProfileByProgramNoAsync(telemetry.CurrentProgram);
        productProfile ??= _pendingContext?.ProductProfile;

        if (productProfile == null) return null!;

        var scanBinding = await ResolveScanBindingAsync("", null, scanner);
        var requireScanRecord = productProfile.ScanConfirmEnabled;
        var requireScanMatch = productProfile.ScanMatchEnabled;

        if (requireScanRecord && string.IsNullOrEmpty(scanBinding.qrCode))
            throw new TestWorkflowException($"scan record is required for {productProfile.ProductModel}", 409);

        if (requireScanMatch)
        {
            try { AssertManualProductMatchesScan(productProfile, scanBinding.qrCode); }
            catch (Exception ex) { throw new TestWorkflowException(ex.Message, 409, ex); }
        }

        if (!string.IsNullOrEmpty(scanBinding.qrCode)) startMode = "scan";

        return new TestContext
        {
            ProductProfile = productProfile,
            Operator = op,
            QrCode = scanBinding.qrCode,
            RecordQrCode = scanBinding.qrCode,
            ScannerEventId = scanBinding.scannerEventId,
            StartMode = startMode
        };
    }

    // ==================== Product / Operator resolution ====================

    private static async Task<ProductProfile> ResolveProductAsync(DatabaseService db, StartPayload payload)
    {
        if (!string.IsNullOrEmpty(payload.ProductModel))
        {
            var productProfile = await db.GetProductProfileByModelAsync(payload.ProductModel)
                ?? throw new TestWorkflowException($"Product model not found: {payload.ProductModel}", 404);
            if (!productProfile.IsActive)
                throw new TestWorkflowException($"Product model is inactive: {payload.ProductModel}", 400);
            return productProfile;
        }

        if (!string.IsNullOrEmpty(payload.QrCode))
        {
            var productProfile = await db.MatchProductProfileByQrAsync(payload.QrCode)
                ?? throw new TestWorkflowException("No product profile matched the QR code", 404);
            return productProfile;
        }

        throw new TestWorkflowException("productModel or qrCode is required", 400);
    }

    private static async Task<Models.Operator?> ResolveOperatorAsync(DatabaseService db, string? operatorName)
    {
        if (string.IsNullOrWhiteSpace(operatorName)) return null;
        var op = await db.GetOperatorByNameAsync(operatorName)
            ?? throw new TestWorkflowException($"Operator not found: {operatorName}", 404);
        if (!op.IsActive)
            throw new TestWorkflowException($"Operator is inactive: {operatorName}", 400);
        return op;
    }

    private async Task<(string qrCode, string? scannerEventId)> ResolveScanBindingAsync(string? explicitQrCode, string? scannerEventId)
    {
        var scope = CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<ScannerService>();
        return await ResolveScanBindingAsync(explicitQrCode, scannerEventId, scanner);
    }

    private Task<(string qrCode, string? scannerEventId)> ResolveScanBindingAsync(string? explicitQrCode, string? scannerEventId, ScannerService scanner)
    {
        var qrCode = explicitQrCode?.Trim() ?? "";
        var evId = scannerEventId?.Trim();

        if (!string.IsNullOrEmpty(qrCode))
        {
            if (!string.IsNullOrEmpty(evId))
                return Task.FromResult<(string, string?)>((qrCode, evId));

            var latest = scanner.GetLatestVisibleScan();
            if (latest != null && latest.RawText == qrCode)
                return Task.FromResult<(string, string?)>((qrCode, latest.Id));

            return Task.FromResult((qrCode, (string?)null));
        }

        var latestScan = scanner.GetLatestVisibleScan();
        if (latestScan == null) return Task.FromResult(("", (string?)null));
        return Task.FromResult<(string, string?)>((latestScan.RawText.Trim(), latestScan.Id));
    }

    // ==================== Assertions ====================

    private static void AssertManualProductMatchesScan(ProductProfile productProfile, string? qrCode)
    {
        if (string.IsNullOrEmpty(productProfile.QrKeyword)) return;
        if (string.IsNullOrEmpty(qrCode) || !qrCode.ToUpperInvariant().Contains(productProfile.QrKeyword.ToUpperInvariant()))
            throw new TestWorkflowException($"QR code does not contain the keyword for {productProfile.ProductModel}: {productProfile.QrKeyword}", 400);
    }

    private static void AssertManualProductHasScan(ProductProfile productProfile, string? qrCode)
    {
        if (string.IsNullOrEmpty(qrCode))
            throw new TestWorkflowException($"Scan record is required for {productProfile.ProductModel}", 400);
    }

    // ==================== Helpers ====================

    private async Task ReleaseStaleArmedContextIfSafeAsync()
    {
        if (!HasArmedPendingContext() || _commandInFlight) return;

        var ageMs = GetArmedContextAgeMs();
        if (ageMs < ArmedContextStaleMs) return;

        var scope = CreateScope();
        var modbus = scope.ServiceProvider.GetRequiredService<ModbusService>();
        try
        {
            var status = await modbus.ReadRealtimeStatusAsync();
            if (status == null || !AteqIdleStepCodes.Contains(status.StepCode)) return;
        }
        catch (Exception ex) when (ex is not ModbusException)
        {
            Console.Error.WriteLine($"[workflow] failed to verify stale armed context: {ex.Message}");
        }

        Console.WriteLine("[workflow] released stale armed context while waiting for step 2");
        _pendingContext = null;
    }

    private bool HasArmedPendingContext() => _pendingContext is { Armed: true };

    private long GetArmedContextAgeMs()
    {
        if (!HasArmedPendingContext()) return 0;
        var armedAt = DateTime.TryParse(_pendingContext!.ArmedAt, out var dt) ? dt : DateTime.MaxValue;
        return (long)(DateTime.UtcNow - armedAt).TotalMilliseconds;
    }

    private static TestContext CreatePendingContext(TestContext context, bool armed)
        => new()
        {
            ProductProfile = context.ProductProfile,
            Operator = context.Operator,
            QrCode = context.QrCode,
            RecordQrCode = context.RecordQrCode,
            ScannerEventId = context.ScannerEventId,
            StartMode = context.StartMode,
            Armed = armed,
            ArmedAt = armed ? DateTime.UtcNow.ToString("o") : null,
            SyncedAt = DateTime.UtcNow.ToString("o")
        };

    private static TestContext CreateSelectedContext(TestContext context)
        => new()
        {
            ProductProfile = context.ProductProfile,
            Operator = context.Operator,
            QrCode = "",
            RecordQrCode = "",
            ScannerEventId = null,
            StartMode = "manual",
            Armed = false
        };

    private static MatchedProduct? ToMatchedProduct(ProductProfile? p)
    {
        if (p == null) return null;
        return new MatchedProduct
        {
            Id = p.Id, ProductModel = p.ProductModel,
            AteqProgramNo = p.AteqProgramNo, QrKeyword = p.QrKeyword
        };
    }

    private static bool IsAteqActiveStep(int stepCode) => stepCode >= 2 && stepCode <= 100;

    private IServiceScope CreateScope() => _scopeFactory.CreateScope();

    private static T DeepClone<T>(T obj) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(obj))!;
}

// ==================== Internal types ====================

public class TestContext
{
    public ProductProfile ProductProfile { get; set; } = null!;
    public Models.Operator? Operator { get; set; }
    public string? QrCode { get; set; }
    public string? RecordQrCode { get; set; }
    public string? ScannerEventId { get; set; }
    public string StartMode { get; set; } = "manual";
    public bool Armed { get; set; }
    public string? ArmedAt { get; set; }
    public string? SyncedAt { get; set; }
}

public class ActiveRun
{
    public ActiveTestState State { get; set; } = null!;
    public bool CancelRequested { get; set; }
}

public class StartPayload
{
    public string? ProductModel { get; set; }
    public string? OperatorName { get; set; }
    public string? QrCode { get; set; }
    public string? ScannerEventId { get; set; }
    public bool? SkipProgramSelect { get; set; }
    public string? StartMode { get; set; }
}

public class ContextPayload
{
    public string ProductModel { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
}

public class TestWorkflowException : Exception
{
    public int StatusCode { get; }
    public Exception? Cause { get; }

    public TestWorkflowException(string message, int statusCode = 400, Exception? cause = null)
        : base(message)
    {
        StatusCode = statusCode;
        Cause = cause;
    }
}

