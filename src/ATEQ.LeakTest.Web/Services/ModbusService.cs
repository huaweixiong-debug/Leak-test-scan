using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Models;

namespace ATEQ.LeakTest.Web.Services;

public class ModbusService
{
    private readonly ModbusRtuClient _client = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly FeatureFlags _flags;
    private CommConfig? _currentConfig;
    private bool _connected;
    private long _lastReconnectAttemptMs;

    public ModbusService(FeatureFlags flags) { _flags = flags; }
    private RealtimeStatus? _lastStatusSnapshot;
    private long _lastStatusSnapshotAt;
    private Task<RealtimeStatus>? _pendingStatusRead;

    // Mock mode state
    private bool _isMock;
    private int _mockStep = 65535;
    private long _mockNextStepAt;
    private int _mockSelectedProgram = 1;
    private string _mockNextResult = "OK";
    private string _mockNextError = "";
    private bool _mockRunCompleted;
    private CancellationTokenSource? _mockRunCts;
    public string MockNextResult { get => _mockNextResult; set => _mockNextResult = value; }
    public string MockNextError { get => _mockNextError; set => _mockNextError = value; }

    public RealtimeStatus? GetLastStatusSnapshot(long maxAgeMs = 0)
    {
        if (_lastStatusSnapshot == null) return null;
        var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastStatusSnapshotAt;
        if (maxAgeMs > 0 && ageMs > maxAgeMs) return null;
        var snapshot = new RealtimeStatus
        {
            Connected = _lastStatusSnapshot.Connected,
            Enabled = _lastStatusSnapshot.Enabled,
            StepCode = _lastStatusSnapshot.StepCode,
            StatusWord = _lastStatusSnapshot.StatusWord,
            CurrentProgram = _lastStatusSnapshot.CurrentProgram,
            Pressure = _lastStatusSnapshot.Pressure,
            PressureUnit = _lastStatusSnapshot.PressureUnit,
            Leak = _lastStatusSnapshot.Leak,
            LeakUnit = _lastStatusSnapshot.LeakUnit,
            ResultCode = _lastStatusSnapshot.ResultCode,
            ErrorCode = _lastStatusSnapshot.ErrorCode,
            ErrorText = _lastStatusSnapshot.ErrorText,
            SnapshotAgeMs = ageMs
        };
        return snapshot;
    }

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> task)
    {
        await _queueLock.WaitAsync();
        try { return await task(); }
        finally { _queueLock.Release(); }
    }

    private async Task ExecuteAsync(Func<Task> task)
    {
        await _queueLock.WaitAsync();
        try { await task(); }
        finally { _queueLock.Release(); }
    }

    // ==================== Configure ====================

    public async Task<object> ConfigureAsync(CommConfig config)
    {
        return await ExecuteAsync(async () =>
        {
            // Save the previous config so we can restore it if reconnection fails.
            // A failed config must never poison the in-memory state — otherwise
            // /api/status keeps returning errorDetail from the wrong port.
            var previousConfig = _currentConfig;
            _currentConfig = null; // ensure EnsureConnectedAsync sees a clean slate until reconnect succeeds

            _lastStatusSnapshot = null;
            _lastStatusSnapshotAt = 0;

            var nextConfig = new CommConfig
            {
                ComPort = config.ComPort,
                Baudrate = config.Baudrate,
                DataBits = config.DataBits,
                Parity = config.Parity?.ToLower() ?? "none",
                StopBits = config.StopBits,
                SlaveId = config.SlaveId ?? 1,
                TimeoutMs = config.TimeoutMs,
                PollIntervalMs = config.PollIntervalMs,
                Dtr = config.Dtr,
                Rts = config.Rts,
                Enabled = config.Enabled
            };

            if (!nextConfig.Enabled)
            {
                _client.Close();
                _connected = false;
                _isMock = false;
                _mockStep = 65535;
                _currentConfig = nextConfig;
                return (object)new { connected = false, enabled = false };
            }

            // Mock mode: skip serial, simulate connected state
            if (string.Equals(nextConfig.ComPort, "MOCK_ATEQ", StringComparison.OrdinalIgnoreCase))
            {
                if (!_flags.EnableMockMode)
                    throw new InvalidOperationException("Mock mode is not enabled. Cannot use MOCK_ATEQ.");

                _client.Close();
                _isMock = true;
                _connected = true;
                _mockStep = 65535;
                _mockNextStepAt = 0;
                _mockRunCompleted = false;
                _mockSelectedProgram = 1;
                _currentConfig = nextConfig;
                Console.WriteLine("[modbus] mock ATEQ enabled");
                return (object)new { connected = true, enabled = true };
            }

            try
            {
                _isMock = false;
                _mockStep = 65535;
                _currentConfig = nextConfig;
                await ReconnectAsync();
                return (object)new { connected = _connected, enabled = true };
            }
            catch
            {
                // Reconnect failed — restore previous config so the runtime
                // doesn't keep trying a bad port (e.g. COM99) after a later
                // successful config (e.g. COM3) is applied.
                _currentConfig = previousConfig;
                throw;
            }
        });
    }

    private async Task ReconnectAsync()
    {
        if (_currentConfig == null) return;
        try
        {
            _client.Close();
            var parity = _currentConfig.Parity?.ToLower() switch
            {
                "none" => System.IO.Ports.Parity.None,
                "even" => System.IO.Ports.Parity.Even,
                "odd" => System.IO.Ports.Parity.Odd,
                "mark" => System.IO.Ports.Parity.Mark,
                "space" => System.IO.Ports.Parity.Space,
                _ => System.IO.Ports.Parity.None
            };
            var stopBits = _currentConfig.StopBits switch
            {
                1 => System.IO.Ports.StopBits.One,
                1.5 => System.IO.Ports.StopBits.OnePointFive,
                2 => System.IO.Ports.StopBits.Two,
                _ => System.IO.Ports.StopBits.One
            };

            await _client.ConnectAsync(
                _currentConfig.ComPort,
                _currentConfig.Baudrate,
                _currentConfig.DataBits,
                parity,
                stopBits,
                _currentConfig.TimeoutMs,
                _currentConfig.Dtr,
                _currentConfig.Rts);

            _connected = true;
            Console.WriteLine($"[modbus] connected {_currentConfig.ComPort} (dtr={_currentConfig.Dtr}, rts={_currentConfig.Rts})");
        }
        catch (Exception ex)
        {
            _connected = false;
            Console.Error.WriteLine($"[modbus] connect failed: {ex.Message}");
            throw new ModbusException("ATEQ serial connect failed", ex);
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_currentConfig == null || !_currentConfig.Enabled)
            throw new ModbusException("ATEQ communication is not enabled");
        if (_isMock) return; // Mock: no serial port to check
        if (!_client.IsOpen || !_connected)
        {
            // Cooldown: don't reconnect faster than every 3 seconds.
            // Rapid open/close cycles can confuse the ATEQ device.
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nowMs - _lastReconnectAttemptMs < 3000)
                throw new ModbusException("ATEQ reconnecting too quickly — waiting for device to stabilize");
            _lastReconnectAttemptMs = nowMs;
            await ReconnectAsync();
        }
    }

    private static bool IsFatalConnectionException(Exception ex)
    {
        if (ex is ModbusException modbusEx)
        {
            if (modbusEx.Message.Contains("Serial port is not open", StringComparison.OrdinalIgnoreCase))
                return true;

            return modbusEx.InnerException != null && IsFatalConnectionException(modbusEx.InnerException);
        }

        if (ex is System.IO.IOException ||
            ex is UnauthorizedAccessException ||
            ex is InvalidOperationException ||
            ex is ObjectDisposedException)
            return true;

        return ex.InnerException != null && IsFatalConnectionException(ex.InnerException);
    }

    private RealtimeStatus BuildMockStatus()
    {
        // Advance mock step sequence if timer elapsed
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var prevStep = _mockStep;
        if (now >= _mockNextStepAt && _mockNextStepAt > 0)
        {
            // Step progression: 65535(idle) stays. After Start: 4 → 5 → 6 → 65535(result)
            if (_mockStep == 65535 && _mockNextStepAt > 0)
            {
                // Was in a run, now at result step with the configured result
                _mockNextStepAt = 0; // stop advancing
            }
            else if (_mockStep == 4) { _mockStep = 5; _mockNextStepAt = now + 1000; }
            else if (_mockStep == 5) { _mockStep = 6; _mockNextStepAt = now + 1000; }
            else if (_mockStep == 6) { _mockStep = 65535; _mockNextStepAt = 0; _mockRunCompleted = true; Console.WriteLine($"[mock] step 6 -> 65535 (result={_mockNextResult})"); }
        }
        if (prevStep != _mockStep)
            Console.WriteLine($"[mock] step advanced: {prevStep} -> {_mockStep}");

        int statusWord = 0;
        double pressure = 0, leak = 0;
        string pressureUnit = "Pa", leakUnit = "cm3/s";
        string resultCode = "UNKNOWN";
        string? errorCode = null;

        if (_mockStep == 4)
        {
            // Fill phase
            pressure = 50.0;
        }
        else if (_mockStep == 5)
        {
            // Stabilize phase
            pressure = 100.0;
            statusWord = ModbusProtocol.FlagKeyPresent;
        }
        else if (_mockStep == 6)
        {
            // Test phase
            pressure = 100.0;
            leak = 0.05;
            statusWord = ModbusProtocol.FlagKeyPresent;
        }
        else if (_mockStep == 65535 && _mockRunCompleted)
        {
            // Result step after a completed run
            pressure = 100.0;
            leak = 0.01;
            resultCode = _mockNextResult;
            errorCode = string.IsNullOrEmpty(_mockNextError) ? null : _mockNextError;
            if (resultCode == "OK")
                statusWord = ModbusProtocol.FlagPassPart | ModbusProtocol.FlagCycleEnd;
            else if (resultCode == "NG")
                statusWord = ModbusProtocol.FlagFailTestPart | ModbusProtocol.FlagCycleEnd;
            if (!string.IsNullOrEmpty(_mockNextError) && _mockNextError == "ATEQ_ALARM")
                statusWord |= ModbusProtocol.FlagAlarm;
            if (!string.IsNullOrEmpty(_mockNextError) && _mockNextError == "ATEQ_PRESSURE_ERROR")
                statusWord |= ModbusProtocol.FlagPressureError;
        }

        return new RealtimeStatus
        {
            Connected = true,
            Enabled = true,
            StepCode = _mockStep,
            StatusWord = statusWord,
            CurrentProgram = _mockSelectedProgram,
            Pressure = pressure,
            PressureUnit = pressureUnit,
            Leak = leak,
            LeakUnit = leakUnit,
            ResultCode = resultCode,
            ErrorCode = errorCode,
            ErrorText = ModbusProtocol.DeriveErrorText(statusWord, errorCode, resultCode)
        };
    }

    // ==================== Read Realtime Status ====================

    public Task<RealtimeStatus> ReadRealtimeStatusAsync()
    {
        // Mock mode: never cache, never dedup. Always return fresh simulated state.
        if (_isMock)
        {
            // Discard any in-flight non-mock read — mock mode doesn't need it.
            _pendingStatusRead = null;

            var mockStatus = BuildMockStatus();
            _lastStatusSnapshot = mockStatus;
            _lastStatusSnapshotAt = 0;
            return Task.FromResult(mockStatus);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Deduplicate concurrent calls: if a read is already in flight, return the same task.
        if (_pendingStatusRead != null)
            return _pendingStatusRead;

        // Only cache successful reads. Never cache errors — a failed read
        // may belong to a previous config (e.g. COM99) that is no longer active.
        if (_lastStatusSnapshot != null && now - _lastStatusSnapshotAt < 120)
            return Task.FromResult(_lastStatusSnapshot);

        _pendingStatusRead = ExecuteAsync(async () =>
        {
            try
            {

                await EnsureConnectedAsync();

                var slaveId = (byte)(_currentConfig?.SlaveId ?? 1);
                var registers = await _client.ReadHoldingRegistersAsync(
                    slaveId, ModbusProtocol.RegRealtimeStatus, ModbusProtocol.RegRealtimeCount);

                var statusWord = ModbusProtocol.Swap16(registers[3]);
                var stepCode = ModbusProtocol.Swap16(registers[4]);
                var currentProgram = ModbusProtocol.Swap16(registers[0]) + 1;
                var pressure = ModbusProtocol.DecodeSignedScaled32(registers[5], registers[6]);
                var pressureUnitCode = ModbusProtocol.DecodeUnitCode(registers[7], registers[8]);
                var leak = ModbusProtocol.DecodeSignedScaled32(registers[9], registers[10]);
                var leakUnitCode = ModbusProtocol.DecodeUnitCode(registers[11], registers[12]);
                var resultCode = ModbusProtocol.DecodeResultCode(statusWord);
                var errorCode = ModbusProtocol.DecodeErrorCode(statusWord);

                var snapshot = new RealtimeStatus
                {
                    Connected = true,
                    Enabled = true,
                    StepCode = stepCode,
                    StatusWord = statusWord,
                    CurrentProgram = currentProgram,
                    Pressure = pressure,
                    PressureUnit = ModbusProtocol.NormalizeUnitLabel(pressureUnitCode),
                    Leak = leak,
                    LeakUnit = ModbusProtocol.NormalizeUnitLabel(leakUnitCode),
                    ResultCode = resultCode,
                    ErrorCode = errorCode,
                    ErrorText = ModbusProtocol.DeriveErrorText(statusWord, errorCode, resultCode)
                };

                // Only cache successful reads (real telemetry from the device).
                // Failed reads are NOT cached — the next call always re-evaluates
                // against the current _currentConfig.
                _lastStatusSnapshot = snapshot;
                _lastStatusSnapshotAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return snapshot;
            }
            catch (Exception ex)
            {
                // A short or garbled Modbus frame is often transient noise, not a hard disconnect.
                // Only tear down the connection for transport-level failures.
                if (IsFatalConnectionException(ex))
                {
                    _connected = false;
                    _client.Close();
                }

                // Do NOT cache this failure — see comment above.
                if (ex is ModbusException) throw;
                throw new ModbusException("ATEQ realtime status read failed", ex);
            }
            finally { _pendingStatusRead = null; }
        });

        return _pendingStatusRead;
    }

    // ==================== Select Program ====================

    public async Task<object> SelectProgramAsync(int programNumber)
    {
        return await ExecuteAsync(async () =>
        {
            await EnsureConnectedAsync();

            if (programNumber < 1 || programNumber > 255)
                throw new ModbusException("ATEQ program number must be between 1 and 255");

            // Mock: store program in memory
            if (_isMock)
            {
                _mockSelectedProgram = programNumber;
                _lastStatusSnapshotAt = 0;
                return (object)new { success = true, programNumber };
            }

            var slaveId = (byte)(_currentConfig!.SlaveId ?? 1);

            // Skip if already selected (within 5s)
            if (_lastStatusSnapshot?.CurrentProgram == programNumber &&
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastStatusSnapshotAt < 5000)
                return (object)new { success = true, programNumber, skipped = true };

            var writeValue = ModbusProtocol.Swap16((ushort)(programNumber - 1));
            await _client.WriteRegisterAsync(slaveId, ModbusProtocol.RegWriteProgram, writeValue);
            _lastStatusSnapshotAt = 0;
            return (object)new { success = true, programNumber };
        });
    }

    // ==================== Start / Reset ====================

    public async Task<object> StartTestAsync()
    {
        return await ExecuteAsync(async () =>
        {
            await EnsureConnectedAsync();

            // Mock: begin simulated run
            if (_isMock)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _mockStep = 4;
                _mockNextStepAt = now + 1000;
                _mockRunCompleted = false;
                Console.WriteLine($"[modbus] mock test started (step={_mockStep})");
                return (object)new { success = true };
            }

            var slaveId = (byte)(_currentConfig!.SlaveId ?? 1);
            await _client.WriteCoilAsync(slaveId, ModbusProtocol.RegStartCoil, true);
            return (object)new { success = true };
        });
    }

    public async Task<object> ResetDeviceAsync()
    {
        return await ExecuteAsync(async () =>
        {
            await EnsureConnectedAsync();

            // Mock: cancel simulated run, return to idle
            if (_isMock)
            {
                _mockRunCts?.Cancel();
                _mockStep = 65535;
                _mockNextStepAt = 0;
                _mockRunCompleted = false;
                Console.WriteLine("[modbus] mock test reset");
                return (object)new { success = true };
            }

            var slaveId = (byte)(_currentConfig!.SlaveId ?? 1);
            await _client.WriteCoilAsync(slaveId, ModbusProtocol.RegResetCoil, true);
            return (object)new { success = true };
        });
    }

    // ==================== Read Program Timings ====================

    public async Task<ProgramTimings> ReadProgramTimingsAsync(int programNumber)
    {
        return await ExecuteAsync(async () =>
        {
            await EnsureConnectedAsync();

            if (programNumber < 1 || programNumber > 255)
                throw new ModbusException("ATEQ program number must be between 1 and 255");

            // Mock: return deterministic timings
            if (_isMock)
            {
                return new ProgramTimings
                {
                    ProgramNumber = programNumber,
                    Source = "mock",
                    FillTimeMs = 5000, StabTimeMs = 3000, TestTimeMs = 2000, DumpTimeMs = 1000,
                    TotalTimeMs = 10000,
                    FillTimeSeconds = 5.0, StabTimeSeconds = 3.0, TestTimeSeconds = 2.0, DumpTimeSeconds = 1.0,
                    TotalTimeSeconds = 10.0
                };
            }

            var slaveId = (byte)(_currentConfig!.SlaveId ?? 1);

            // Select program for editing
            await _client.WriteRegisterAsync(slaveId, ModbusProtocol.RegEditProgram,
                ModbusProtocol.Swap16((ushort)(programNumber - 1)));
            await Task.Delay(300);

            // Request timing parameters by identifier
            var paramIds = ModbusProtocol.ProgramTimingParameterIds.Values.ToArray();
            var requestWords = new ushort[paramIds.Length + 1];
            requestWords[0] = (ushort)paramIds.Length;
            for (int i = 0; i < paramIds.Length; i++) requestWords[i + 1] = (ushort)paramIds[i];
            for (int i = 0; i < requestWords.Length; i++) requestWords[i] = ModbusProtocol.Swap16(requestWords[i]);
            await _client.WriteRegistersAsync(slaveId, 0x0000, requestWords);
            await Task.Delay(150);

            var response = await _client.ReadHoldingRegistersAsync(slaveId, 0x0000, (ushort)(paramIds.Length * 3));
            var timingsMs = new Dictionary<string, int?>();
            for (int i = 0; i < paramIds.Length; i++)
            {
                var baseOffset = i * 3;
                if (baseOffset + 2 >= response.Length) break;
                var paramId = ModbusProtocol.Swap16(response[baseOffset]);
                var valueMs = ModbusProtocol.DecodeSwappedUnsigned32(response[baseOffset + 1], response[baseOffset + 2]);
                var fieldName = ModbusProtocol.ProgramTimingParameterIds
                    .FirstOrDefault(kv => kv.Value == paramId).Key;
                if (fieldName != null)
                    timingsMs[fieldName] = ModbusProtocol.NormalizeTimingValue(valueMs);
            }

            var identifierPayload = BuildProgramTimingsPayload(programNumber, timingsMs, "parameter-identifiers");
            if (HasCoreProgramTimings(timingsMs))
            {
                identifierPayload.Diagnostics = new ProgramTimingsDiagnostics
                {
                    PrimarySource = identifierPayload.Source
                };
                return identifierPayload;
            }

            // Fallback: direct register read
            ProgramTimings? directPayload = null;
            try
            {
                var directResponse = await _client.ReadHoldingRegistersAsync(slaveId, 0x0400, 9);
                var directTimingsMs = new Dictionary<string, int?>
                {
                    ["fillTime"] = directResponse.Length > 0 ? ModbusProtocol.NormalizeTimingValue(ModbusProtocol.Swap16(directResponse[0])) : null,
                    ["stabTime"] = directResponse.Length > 1 ? ModbusProtocol.NormalizeTimingValue(ModbusProtocol.Swap16(directResponse[1])) : null,
                    ["testTime"] = directResponse.Length > 2 ? ModbusProtocol.NormalizeTimingValue(ModbusProtocol.Swap16(directResponse[2])) : null,
                    ["dumpTime"] = directResponse.Length > 8 ? ModbusProtocol.NormalizeTimingValue(ModbusProtocol.Swap16(directResponse[8])) : null
                };
                directPayload = BuildProgramTimingsPayload(programNumber, directTimingsMs, "direct-registers");
            }
            catch { /* direct read is optional */ }

            if (directPayload != null && CountProgramTimings(directPayload) > 0)
            {
                directPayload.Diagnostics = new ProgramTimingsDiagnostics
                {
                    PrimarySource = "direct-registers",
                    FallbackSource = "parameter-identifiers",
                    FallbackTimings = new FallbackTimings
                    {
                        FillTimeMs = identifierPayload.FillTimeMs,
                        StabTimeMs = identifierPayload.StabTimeMs,
                        TestTimeMs = identifierPayload.TestTimeMs,
                        DumpTimeMs = identifierPayload.DumpTimeMs
                    }
                };
                return directPayload;
            }

            identifierPayload.Diagnostics = new ProgramTimingsDiagnostics
            {
                PrimarySource = "parameter-identifiers",
                FallbackSource = directPayload != null ? "direct-registers" : null,
                FallbackTimings = directPayload != null ? new FallbackTimings
                {
                    FillTimeMs = directPayload.FillTimeMs,
                    StabTimeMs = directPayload.StabTimeMs,
                    TestTimeMs = directPayload.TestTimeMs,
                    DumpTimeMs = directPayload.DumpTimeMs
                } : null
            };
            return identifierPayload;
        });
    }

    // ==================== Timing helpers ====================

    private static ProgramTimings BuildProgramTimingsPayload(int programNumber, Dictionary<string, int?> timingsMs, string source)
    {
        var fillTimeMs = timingsMs.GetValueOrDefault("fillTime");
        var stabTimeMs = timingsMs.GetValueOrDefault("stabTime");
        var testTimeMs = timingsMs.GetValueOrDefault("testTime");
        var dumpTimeMs = timingsMs.GetValueOrDefault("dumpTime");

        var coreTimes = new[] { fillTimeMs, stabTimeMs, testTimeMs }
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var totalTimeMs = coreTimes.Count > 0 ? coreTimes.Sum() : (int?)null;

        return new ProgramTimings
        {
            ProgramNumber = programNumber,
            Source = source,
            FillTimeMs = fillTimeMs,
            StabTimeMs = stabTimeMs,
            TestTimeMs = testTimeMs,
            DumpTimeMs = dumpTimeMs,
            TotalTimeMs = totalTimeMs,
            FillTimeSeconds = fillTimeMs.HasValue ? fillTimeMs.Value / 1000.0 : null,
            StabTimeSeconds = stabTimeMs.HasValue ? stabTimeMs.Value / 1000.0 : null,
            TestTimeSeconds = testTimeMs.HasValue ? testTimeMs.Value / 1000.0 : null,
            DumpTimeSeconds = dumpTimeMs.HasValue ? dumpTimeMs.Value / 1000.0 : null,
            TotalTimeSeconds = totalTimeMs.HasValue && totalTimeMs.Value > 0 ? totalTimeMs.Value / 1000.0 : null
        };
    }

    private static bool HasCoreProgramTimings(Dictionary<string, int?> timingsMs)
        => timingsMs.GetValueOrDefault("fillTime") is int &&
           timingsMs.GetValueOrDefault("stabTime") is int &&
           timingsMs.GetValueOrDefault("testTime") is int;

    private static int CountProgramTimings(ProgramTimings p)
        => new[] { p.FillTimeMs, p.StabTimeMs, p.TestTimeMs, p.DumpTimeMs }.Count(v => v.HasValue);
}
