using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Models;

namespace ATEQ.LeakTest.Web.Services;

public class PlcService
{
    public const int StaleFallbackMs = 5000;

    public sealed class PlcCachedSnapshot<T>
    {
        public T Data { get; init; } = default!;
        public long CapturedAtMs { get; init; }
        public int AgeMs => (int)(Environment.TickCount64 - CapturedAtMs);
        public bool IsFresh => AgeMs <= StaleFallbackMs;

        public static PlcCachedSnapshot<T> Capture(T data) => new()
        {
            Data = data,
            CapturedAtMs = Environment.TickCount64
        };
    }

    private PlcCachedSnapshot<object>? _cachedStatus;
    private PlcCachedSnapshot<object>? _cachedManualMonitor;

    private sealed record PlcMonitorPoint(
        string Label,
        ushort Address,
        string Description,
        string OnText,
        string OffText,
        bool Writable);

    private static readonly PlcMonitorPoint[] ManualControlPoints =
    [
        new("M0", 8192, "启动许可状态", "允许启动", "未允许", false),
        new("M10", 8202, "手动模式", "手动开启", "手动关闭", true),
        new("M11", 8203, "移载气缸", "前进", "后退", true),
        new("M12", 8204, "下压气缸", "下压", "上升", true)
    ];

    private static readonly PlcMonitorPoint[] AlarmPoints =
    [
        new("M20", 8212, "移载前进不到位报警", "报警", "正常", false),
        new("M21", 8213, "下压上升不到位报警", "报警", "正常", false),
        new("M22", 8214, "下压下降不到位报警", "报警", "正常", false),
        new("M23", 8215, "安全光栅报警", "报警", "正常", false),
        new("M24", 8216, "急停报警", "报警", "正常", false)
    ];

    private static readonly PlcMonitorPoint[] InputPoints =
    [
        new("X0", 0, "启动按钮", "按下", "释放", false),
        new("X1", 1, "复位按钮", "按下", "释放", false),
        new("X2", 2, "急停按钮", "按下", "释放", false),
        new("X3", 3, "安全光栅", "触发", "正常", false),
        new("X4", 4, "上升到位", "到位", "未到位", false),
        new("X5", 5, "移载前进到位", "到位", "未到位", false),
        new("X6", 6, "辊道进口传感器", "有料", "无料", false),
        new("X7", 7, "辊道满料传感器", "满料", "未满", false),
        new("X10", 8, "下降到位", "到位", "未到位", false)
    ];

    private readonly PlcModbusTcpClient _client = new();
    private string _host = "192.168.2.1";
    private int _port = 502;
    private byte _unitId = 1;
    private bool _connected;

    // Active config (populated by ConfigureAsync)
    public ushort AddrM1 { get; private set; } = 8193;
    public ushort AddrM2 { get; private set; } = 8194;
    public ushort AddrM3 { get; private set; } = 8195;
    public ushort AddrM4 { get; private set; } = 8196;
    public int PollIntervalMs { get; private set; } = 250;

    public bool IsConnected => _connected && _client.IsOpen;
    public string Host => _host;
    public int Port => _port;
    public byte UnitId => _unitId;

    /// <summary>Dynamic address → label lookup built from current config.</summary>
    private Dictionary<ushort, string> BuildAddressLabels()
    {
        var map = new Dictionary<ushort, string>
        {
            [AddrM1] = "M1",
            [AddrM2] = "M2",
            [AddrM3] = "M3",
            [AddrM4] = "M4"
        };
        return map;
    }

    /// <summary>M1 and M4 are always read-only inputs.</summary>
    private HashSet<ushort> ReadOnlyAddresses() => [AddrM1, AddrM4];

    // ==================== Connect / Disconnect ====================

    public async Task<object> ConnectAsync(string host, int port, byte unitId)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
        await _client.ConnectAsync(host, port);
        _connected = true;
        return new { connected = true, host, port, unitId };
    }

    public void Disconnect()
    {
        _client.Disconnect();
        _connected = false;
    }

    // ==================== Configure from PlcConfig ====================

    public async Task<object> ConfigureAsync(PlcConfig config)
    {
        _host = config.Host;
        _port = config.Port;
        _unitId = config.UnitId;
        PollIntervalMs = config.PollIntervalMs > 0 ? config.PollIntervalMs : 250;
        AddrM1 = config.StartAddressM1 > 0 ? config.StartAddressM1 : (ushort)8193;
        AddrM2 = config.OkAddressM2 > 0 ? config.OkAddressM2 : (ushort)8194;
        AddrM3 = config.NgAddressM3 > 0 ? config.NgAddressM3 : (ushort)8195;
        AddrM4 = config.ResetAddressM4 > 0 ? config.ResetAddressM4 : (ushort)8196;

        if (!config.Enabled)
        {
            Disconnect();
            return new { configured = true, connected = false, reason = "PLC disabled in config" };
        }

        try
        {
            await ConnectAsync(_host, _port, _unitId);
            return new { configured = true, connected = true, host = _host, port = _port, unitId = _unitId };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[plc] configure: connection failed — {ex.Message}");
            return new { configured = true, connected = false, reason = ex.Message };
        }
    }

    // ==================== I/O Snapshot (address-driven) ====================

    /// <summary>
    /// Read M1-M4 using the configured addresses. Uses a single contiguous read
    /// if the addresses are within a reasonable span; falls back to individual reads.
    /// </summary>
    public async Task<PlcIoSnapshot> ReadIoCoilsAsync()
    {
        if (!IsConnected) throw new ModbusException("PLC not connected");

        var addrs = new[] { AddrM1, AddrM2, AddrM3, AddrM4 };
        var min = addrs.Min();
        var max = addrs.Max();
        var span = max - min + 1;

        bool[] all;
        if (span <= 8)
        {
            // Contiguous or near-contiguous: one read, then map by offset
            var raw = await _client.ReadCoilsAsync(_unitId, min, (ushort)span);
            all = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                var offset = addrs[i] - min;
                all[i] = offset < raw.Length && raw[offset];
            }
        }
        else
        {
            // Addresses are far apart — individual reads
            all = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                var one = await _client.ReadCoilsAsync(_unitId, addrs[i], 1);
                all[i] = one.Length > 0 && one[0];
            }
        }

        return new PlcIoSnapshot { M1 = all[0], M2 = all[1], M3 = all[2], M4 = all[3] };
    }

    // ==================== Output helpers ====================

    public async Task<bool> WriteOkAsync(bool value)
    {
        if (!IsConnected) return false;
        await _client.WriteCoilAsync(_unitId, AddrM2, value);
        var actual = await _client.ReadCoilsAsync(_unitId, AddrM2, 1);
        return actual.Length > 0 && actual[0] == value;
    }

    public async Task<bool> WriteNgAsync(bool value)
    {
        if (!IsConnected) return false;
        await _client.WriteCoilAsync(_unitId, AddrM3, value);
        var actual = await _client.ReadCoilsAsync(_unitId, AddrM3, 1);
        return actual.Length > 0 && actual[0] == value;
    }

    public async Task<bool> WriteM0Async(bool value)
    {
        if (!IsConnected) return false;
        await _client.WriteCoilAsync(_unitId, 8192, value);
        var actual = await _client.ReadCoilsAsync(_unitId, 8192, 1);
        var ok = actual.Length > 0 && actual[0] == value;
        if (ok) Console.WriteLine($"[plc] M0 (scan OK) = {(value ? "ON" : "OFF")}");
        return ok;
    }

    public async Task ClearOutputsAsync()
    {
        if (!IsConnected) return;
        await _client.WriteCoilAsync(_unitId, 8192, false);     // M0 scan OK
        await _client.WriteCoilAsync(_unitId, AddrM2, false);   // M2 result OK
        await _client.WriteCoilAsync(_unitId, AddrM3, false);   // M3 result NG
        Console.WriteLine("[plc] M0/M2/M3 cleared");
    }

    // ==================== Status (address-driven) ====================

    public PlcCachedSnapshot<object>? GetLastStatusSnapshot() => _cachedStatus?.IsFresh == true ? _cachedStatus : null;
    public PlcCachedSnapshot<object>? GetLastManualMonitorSnapshot() => _cachedManualMonitor?.IsFresh == true ? _cachedManualMonitor : null;

    public async Task<object> GetStatusAsync()
    {
        if (!IsConnected)
            return new { connected = false };

        var snapshot = await ReadIoCoilsAsync();
        var result = (object)new
        {
            connected = true,
            host = _host,
            port = _port,
            unitId = _unitId,
            coils = new Dictionary<string, object>
            {
                ["M1"] = new { address = AddrM1, value = snapshot.M1, writable = false },
                ["M2"] = new { address = AddrM2, value = snapshot.M2, writable = true },
                ["M3"] = new { address = AddrM3, value = snapshot.M3, writable = true },
                ["M4"] = new { address = AddrM4, value = snapshot.M4, writable = false }
            }
        };
        _cachedStatus = PlcCachedSnapshot<object>.Capture(result);
        return result;
    }

    public async Task<object> ReadManualMonitorAsync()
    {
        if (!IsConnected)
            throw new ModbusException("PLC not connected");

        const ushort m0Address = 8192;
        const ushort monitorCoilStart = 8202;
        const ushort monitorCoilCount = 15;
        const ushort monitorInputStart = 0;
        const ushort monitorInputCount = 9;

        var m0Value = await _client.ReadCoilsAsync(_unitId, m0Address, 1);
        var coilValues = await _client.ReadCoilsAsync(_unitId, monitorCoilStart, monitorCoilCount);
        var inputValues = await _client.ReadDiscreteInputsAsync(_unitId, monitorInputStart, monitorInputCount);

        static object BuildPoint(PlcMonitorPoint point, bool value) => new
        {
            label = point.Label,
            address = point.Address,
            description = point.Description,
            onText = point.OnText,
            offText = point.OffText,
            writable = point.Writable,
            value
        };

        bool ReadCoilPoint(PlcMonitorPoint point) => coilValues[point.Address - monitorCoilStart];
        bool ReadInputPoint(PlcMonitorPoint point) => inputValues[point.Address - monitorInputStart];

        var controls = new List<object>
        {
            BuildPoint(ManualControlPoints[0], m0Value.Length > 0 && m0Value[0])
        };
        controls.AddRange(ManualControlPoints.Skip(1).Select(point => BuildPoint(point, ReadCoilPoint(point))));

        var result = (object)new
        {
            connected = true,
            host = _host,
            port = _port,
            unitId = _unitId,
            controls = controls.ToArray(),
            alarms = AlarmPoints.Select(point => BuildPoint(point, ReadCoilPoint(point))).ToArray(),
            inputs = InputPoints.Select(point => BuildPoint(point, ReadInputPoint(point))).ToArray()
        };
        _cachedManualMonitor = PlcCachedSnapshot<object>.Capture(result);
        return result;
    }

    // ==================== Read Map (address-driven) ====================

    public async Task<object> ReadMapAsync()
    {
        if (!IsConnected)
            throw new ModbusException("PLC not connected");

        var snapshot = await ReadIoCoilsAsync();
        var all = snapshot.All; // [M1, M2, M3, M4]
        var labels = new[] { "M1", "M2", "M3", "M4" };
        var addrs = new[] { AddrM1, AddrM2, AddrM3, AddrM4 };
        var writable = new[] { false, true, true, false };
        var items = new List<object>();
        var values = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            values.Add(all[i]);
            items.Add(new { label = labels[i], address = addrs[i], value = all[i], writable = writable[i] });
        }
        // Include M0 for plc-test.html backward compat (always address 8192)
        var m0addr = (ushort)8192;
        try
        {
            var m0 = await _client.ReadCoilsAsync(_unitId, m0addr, 1);
            var m0val = m0.Length > 0 && m0[0];
            values.Insert(0, m0val);
            items.Insert(0, new { label = "M0", address = m0addr, value = m0val, writable = true });
        }
        catch
        {
            values.Insert(0, false);
            items.Insert(0, new { label = "M0", address = m0addr, value = false, writable = true });
        }
        return new { unitId = _unitId, startAddress = (ushort)8192, values, coils = items };
    }

    // ==================== Write Coil (for plc-test.html) ====================

    public async Task<object> WriteCoilAsync(string label, bool value)
    {
        if (!IsConnected)
            throw new ModbusException("PLC not connected");

        var upper = label.Trim().ToUpperInvariant();
        if (!TryResolveWritableCoil(label, out var address))
            throw new ModbusException("Unknown or read-only coil. Writable labels: M0, M2, M3, M10, M11, M12");

        await _client.WriteCoilAsync(_unitId, address, value);
        var coils = await _client.ReadCoilsAsync(_unitId, address, 1);
        var actual = coils.Length > 0 && coils[0];

        return new { label = upper, address, requested = value, actual };
    }

    // ==================== Reset Outputs ====================

    public async Task<object> ResetOutputsAsync()
    {
        if (!IsConnected)
            throw new ModbusException("PLC not connected");

        var results = new List<object>();
        // Only write to output coils (M0 for test, M2, M3)
        var outputs = new[] { ((ushort)8192, "M0"), (AddrM2, "M2"), (AddrM3, "M3") };
        foreach (var (addr, label) in outputs)
        {
            await _client.WriteCoilAsync(_unitId, addr, false);
            results.Add(new { label, address = addr, written = false });
        }
        return new { message = "All writable outputs turned OFF", results };
    }

    public async Task<object> ResetManualOutputsAsync()
    {
        if (!IsConnected)
            throw new ModbusException("PLC not connected");

        var results = new List<object>();
        foreach (var point in ManualControlPoints)
        {
            await _client.WriteCoilAsync(_unitId, point.Address, false);
            var actual = await _client.ReadCoilsAsync(_unitId, point.Address, 1);
            results.Add(new
            {
                label = point.Label,
                address = point.Address,
                requested = false,
                actual = actual.Length > 0 && actual[0]
            });
        }

        return new { message = "Manual control coils reset", results };
    }

    private bool TryResolveWritableCoil(string label, out ushort address)
    {
        var upper = label.Trim().ToUpperInvariant();
        switch (upper)
        {
            case "M0":
                address = 8192;
                return true;
            case "M2":
                address = AddrM2;
                return true;
            case "M3":
                address = AddrM3;
                return true;
            case "M10":
            case "M11":
            case "M12":
                if (int.TryParse(upper[1..], out var bitNumber))
                {
                    address = (ushort)(8192 + bitNumber);
                    return true;
                }
                break;
        }

        address = 0;
        return false;
    }
}
