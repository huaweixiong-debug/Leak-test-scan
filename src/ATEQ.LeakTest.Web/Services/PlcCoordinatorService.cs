using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Models;

namespace ATEQ.LeakTest.Web.Services;

public class PlcCoordinatorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlcService _plc;
    private readonly TestWorkflowService _workflow;
    private readonly object _lock = new();
    private Task? _loop;
    private CancellationTokenSource? _cts;

    // Edge detection state
    private bool _lastM1;
    private bool _lastM4;
    private bool _edgesSeeded;

    // Output dedup
    private string? _lastWrittenRecordId;
    private string? _lastLiveNgRunKey;
    private bool _outputsCleared = true;

    // Auto-reconnect
    private long _lastReconnectAttemptAt; // Environment.TickCount64
    private const int ReconnectBackoffMs = 5000;

    // Public state
    public bool Online { get; private set; }
    public DateTime LastPollAt { get; private set; } = DateTime.MinValue;
    public string? LastError { get; private set; }

    public PlcCoordinatorService(IServiceScopeFactory scopeFactory, PlcService plc, TestWorkflowService workflow)
    {
        _scopeFactory = scopeFactory;
        _plc = plc;
        _workflow = workflow;
    }

    public bool IsRunning => _loop != null && !_loop.IsCompleted;

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _edgesSeeded = false;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
            Console.WriteLine("[plc-coord] started");
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_lock)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }

        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (loop != null)
        {
            try { await loop; }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { Console.WriteLine($"[plc-coord] stop: loop exit with {ex.Message}"); }
        }

        _outputsCleared = true;
        _lastWrittenRecordId = null;
        _lastLiveNgRunKey = null;
        _edgesSeeded = false;
        Online = false;
        Console.WriteLine("[plc-coord] stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_plc.PollIntervalMs, ct);
                await PollOnceAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Online = false;
                Console.WriteLine($"[plc-coord] poll error: {ex.Message}");
            }
        }
    }

    private async Task PollOnceAsync()
    {
        // --- Auto-reconnect if disconnected ---
        if (!_plc.IsConnected)
        {
            await TryReconnectAsync();
            return;
        }

        PlcIoSnapshot snapshot;
        try
        {
            snapshot = await _plc.ReadIoCoilsAsync();
        }
        catch (ModbusException ex)
        {
            Online = false;
            LastError = ex.Message;
            return;
        }

        Online = true;
        LastError = null;
        LastPollAt = DateTime.UtcNow;

        // Seed edge state on first successful poll after (re)connect
        if (!_edgesSeeded)
        {
            _lastM1 = snapshot.M1;
            _lastM4 = snapshot.M4;
            _edgesSeeded = true;
            Console.WriteLine($"[plc-coord] edges seeded: M1={_lastM1} M4={_lastM4}");
            await MaybeWriteLiveNgOutputAsync();
            await MaybeWriteResultOutputsAsync();
            return;
        }

        // --- M4 rising edge: reset ---
        if (snapshot.M4 && !_lastM4)
        {
            Console.WriteLine("[plc-coord] M4 rising edge → RESET + clear M2/M3");
            await HandleM4RisingEdgeAsync();
        }

        // --- M1 rising edge: start test ---
        if (snapshot.M1 && !_lastM1)
        {
            Console.WriteLine("[plc-coord] M1 rising edge → request start");
            await HandleM1RisingEdgeAsync();
        }

        _lastM1 = snapshot.M1;
        _lastM4 = snapshot.M4;

        // --- Observe test completion → write M2/M3 ---
        await MaybeWriteLiveNgOutputAsync();
        await MaybeWriteResultOutputsAsync();
    }

    // ==================== Auto-reconnect ====================

    private async Task TryReconnectAsync()
    {
        Online = false;

        var now = Environment.TickCount64;
        if (now - _lastReconnectAttemptAt < ReconnectBackoffMs)
            return; // backoff

        _lastReconnectAttemptAt = now;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();

        PlcConfig? config;
        try { config = await db.GetPlcConfigAsync(); }
        catch (Exception ex)
        {
            LastError = $"Failed to read PLC config: {ex.Message}";
            return;
        }

        if (config == null || !config.Enabled)
        {
            LastError = "PLC disabled in config";
            return;
        }

        Console.WriteLine("[plc-coord] attempting reconnect...");
        try
        {
            var result = await _plc.ConfigureAsync(config);
            dynamic r = result;
            if (r.connected == true)
            {
                Console.WriteLine("[plc-coord] reconnected successfully");
                // Seed edge state from current values to avoid false rising edges
                try
                {
                    var snapshot = await _plc.ReadIoCoilsAsync();
                    _lastM1 = snapshot.M1;
                    _lastM4 = snapshot.M4;
                    _edgesSeeded = true;
                }
                catch { _edgesSeeded = false; }
                Online = true;
                LastError = null;
                await _workflow.RestoreScanFreeM0IfNeededAsync();
            }
            else
            {
                LastError = $"Reconnect failed: {r.reason}";
            }
        }
        catch (Exception ex)
        {
            LastError = $"Reconnect exception: {ex.Message}";
        }
    }

    // ==================== M1: start test ====================

    private async Task HandleM1RisingEdgeAsync()
    {
        try
        {
            var activeState = _workflow.GetActiveState();
            if (activeState.Running)
            {
                Console.WriteLine("[plc-coord] M1 ignored: test already running");
                return;
            }
            if (activeState.Stage == "armed")
            {
                Console.WriteLine("[plc-coord] M1 ignored: armed pending context exists");
                return;
            }

            var result = await _workflow.StartFromSelectedContextAsync("plc");
            if (result == null)
            {
                Console.WriteLine("[plc-coord] M1 ignored: no selected context (select product + operator first)");
            }
            else
            {
                Console.WriteLine("[plc-coord] M1 → test started via PLC trigger");
                _outputsCleared = false;
                _lastWrittenRecordId = null;
                _lastLiveNgRunKey = null;
                // Clear M0 (scan OK) at the start of a new test cycle
                try { await _plc.WriteM0Async(false); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[plc-coord] M1 start failed: {ex.Message}");
        }
    }

    // ==================== M4: reset ====================

    private async Task HandleM4RisingEdgeAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var modbus = scope.ServiceProvider.GetRequiredService<ModbusService>();
            try { await modbus.ResetDeviceAsync(); }
            catch (Exception ex) { Console.WriteLine($"[plc-coord] ATEQ reset failed: {ex.Message}"); }

            _workflow.HandleResetCommand();
            await _plc.ClearOutputsAsync();
            _outputsCleared = true;
            _lastWrittenRecordId = null;
            _lastLiveNgRunKey = null;

            // Restore M0 if scan-free product is still selected after reset
            await _workflow.RestoreScanFreeM0IfNeededAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[plc-coord] M4 reset failed: {ex.Message}");
        }
    }

    private async Task MaybeWriteLiveNgOutputAsync()
    {
        try
        {
            var state = _workflow.GetActiveState();
            if (!state.Running) return;

            var telemetry = state.LatestTelemetry;
            if (telemetry == null) return;

            if (telemetry.StepCode < 2 || telemetry.StepCode > 6) return;

            var resultCode = (telemetry.ResultCode ?? "").Trim().ToUpperInvariant();
            if (resultCode != "NG") return;

            var runKey = state.StartedAt;
            if (string.IsNullOrWhiteSpace(runKey) || runKey == _lastLiveNgRunKey) return;

            var writeSucceeded = await _plc.WriteNgAsync(true);
            if (!writeSucceeded)
            {
                Console.WriteLine($"[plc-coord] live M3 write not confirmed at step {telemetry.StepCode}, will retry");
                return;
            }

            _lastLiveNgRunKey = runKey;
            _outputsCleared = false;
            Console.WriteLine($"[plc-coord] M3=ON (live NG) at step {telemetry.StepCode}, result={resultCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[plc-coord] live NG output write failed: {ex.Message}");
        }
    }

    // ==================== M2/M3: result outputs ====================

    private async Task MaybeWriteResultOutputsAsync()
    {
        try
        {
            var state = _workflow.GetActiveState();
            if (state.Running) return;
            if (_outputsCleared == false && _lastWrittenRecordId != null) return;

            var savedId = state.SavedRecord?.Id;
            if (string.IsNullOrEmpty(savedId)) return;
            if (savedId == _lastWrittenRecordId) return;

            var isOk = string.Equals(state.ResultCode, "OK", StringComparison.OrdinalIgnoreCase);

            if (!isOk && !string.IsNullOrWhiteSpace(state.StartedAt) && state.StartedAt == _lastLiveNgRunKey)
            {
                _lastWrittenRecordId = savedId;
                _outputsCleared = false;
                Console.WriteLine($"[plc-coord] M3 already latched from live NG for record {savedId}");
                return;
            }

            if (string.IsNullOrEmpty(state.ResultCode) || state.ResultCode.Trim().Length == 0)
                Console.WriteLine($"[plc-coord] result missing for record {savedId} — treating as NG");

            bool writeSucceeded;
            if (isOk)
            {
                writeSucceeded = await _plc.WriteOkAsync(true);
                if (writeSucceeded)
                    Console.WriteLine($"[plc-coord] M2=ON (OK) for record {savedId}");
            }
            else
            {
                writeSucceeded = await _plc.WriteNgAsync(true);
                if (writeSucceeded)
                    Console.WriteLine($"[plc-coord] M3=ON (NG) for record {savedId} result={state.ResultCode}");
            }

            if (!writeSucceeded)
            {
                Console.WriteLine($"[plc-coord] result output write not confirmed for record {savedId}, will retry");
                return;
            }

            _lastWrittenRecordId = savedId;
            _outputsCleared = false;

            // Restore M0 if scan-free product is still selected
            await _workflow.RestoreScanFreeM0IfNeededAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[plc-coord] result output write failed: {ex.Message}");
        }
    }
}
