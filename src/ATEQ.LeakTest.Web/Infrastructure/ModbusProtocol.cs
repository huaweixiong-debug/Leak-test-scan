namespace ATEQ.LeakTest.Web.Infrastructure;

/// <summary>
/// Modbus protocol constants, decode helpers, and unit mappings.
/// 1:1 port of modbusService.js constants + pure functions.
/// </summary>
public static class ModbusProtocol
{
    // ==================== Register map ====================

    public const int RegWriteProgram = 0x0200;
    public const int RegReadProgram = 0x0202;
    public const int RegEditProgram = 0x3004;
    public const int RegStepCode = 0x0020;
    public const int RegRealtimeStatus = 0x0030;
    public const int RegRealtimeCount = 13;
    public const int RegResetCoil = 0x0000;
    public const int RegStartCoil = 0x0001;

    public static readonly Dictionary<string, int> ProgramTimingParameterIds = new()
    {
        ["fillTime"] = 0x0001,
        ["stabTime"] = 0x0002,
        ["testTime"] = 0x0003,
        ["dumpTime"] = 0x0009
    };

    // ==================== Unit code map ====================

    public static readonly Dictionary<int, string> UnitCodeMap = new()
    {
        [0] = "cm3/s", [1000] = "cm3/min", [2000] = "cm3/h",
        [3000] = "mm3/s", [4000] = "Pa(Cal.)", [5000] = "Pa/s(Cal.)",
        [6000] = "Pa", [7000] = "Pa(HR)", [8000] = "Pa/s",
        [9000] = "Pa/s(HR)", [11000] = "Bar", [12000] = "kPa",
        [13000] = "PSI", [14000] = "mBar", [15000] = "MPa",
        [43000] = "Pa(D)", [44000] = "Pa(LR)", [45000] = "Pa/s(LR)",
        [46000] = "in3/s", [47000] = "in3/min", [48000] = "in3/h",
        [49000] = "ft3/h", [50000] = "mL/s", [51000] = "mL/min",
        [52000] = "mL/h", [58000] = "cm3/s", [59000] = "cm3/min",
        [60000] = "cm3/h", [76000] = "ft3/s", [77000] = "ft3/min"
    };

    public static readonly Dictionary<string, string> UnitLabelAliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CALIBRATED PA"] = "Pa(Cal.)", ["CALIBRATED PA/S"] = "Pa/s(Cal.)",
        ["HIGH RESOLUTION PA"] = "Pa(HR)", ["HIGH RESOLUTION PA/S"] = "Pa/s(HR)",
        ["LOW RESOLUTION PA"] = "Pa(LR)", ["LOW RESOLUTION PA/S"] = "Pa/s(LR)",
        ["D MODE PA"] = "Pa(D)", ["PA(CAL.)"] = "Pa(Cal.)", ["PA/S(CAL.)"] = "Pa/s(Cal.)",
        ["PA(HR)"] = "Pa(HR)", ["PA/S(HR)"] = "Pa/s(HR)",
        ["PA(LR)"] = "Pa(LR)", ["PA/S(LR)"] = "Pa/s(LR)", ["PA(D)"] = "Pa(D)",
        ["MM3/H"] = "mm3/s"
    };

    // ==================== Alarm code map ====================

    public static readonly Dictionary<int, string> AlarmCodeMap = new()
    {
        [0] = "No alarm", [1] = "Pressure switched alarm (test pressure too high)",
        [2] = "Pressure switch (test pressure too small)",
        [3] = "Large leak on TEST (EEEE)", [4] = "Large leak on REF (MMMM)",
        [7] = "Sensor out of order (overrun)", [8] = "ATR error", [9] = "ATR drift",
        [10] = "CAL error", [11] = "Volume too small (sealed component)",
        [12] = "Volume too large (sealed component)", [14] = "Equalization valve switching error",
        [43] = "Pressure too high", [44] = "Pressure too low",
        [45] = "Piezo sensor out of order", [46] = "Dump error",
        [47] = "CAL drift error", [48] = "Calibration check error",
        [49] = "Leak in calibration check too high", [50] = "Leak in calibration check too low",
        [51] = "Sealed component learning error"
    };

    // ==================== Status word flags ====================

    public const int FlagPassPart = 0x0001;
    public const int FlagFailTestPart = 0x0002;
    public const int FlagFailReferencePart = 0x0004;
    public const int FlagAlarm = 0x0008;
    public const int FlagPressureError = 0x0010;
    public const int FlagCycleEnd = 0x0020;
    public const int FlagKeyPresent = 0x8000;

    public static readonly Dictionary<string, string> ErrorCodeTextMap = new()
    {
        ["ATEQ_PRESSURE_ERROR"] = "压力异常",
        ["ATEQ_ALARM"] = "仪器报警"
    };

    // ==================== Raw data helpers ====================

    public static ushort Swap16(ushort value) => (ushort)(((value & 0xff) << 8) | ((value >> 8) & 0xff));

    public static uint CombineSwappedUnsigned32(ushort lowWord, ushort highWord)
    {
        var low = Swap16(lowWord);
        var high = Swap16(highWord);
        return (uint)((high * 0x10000) + low);
    }

    public static double DecodeSignedScaled32(ushort lowWord, ushort highWord)
    {
        long raw = CombineSwappedUnsigned32(lowWord, highWord);
        if (raw >= 0x80000000) raw -= 0x100000000;
        return raw / 1000.0;
    }

    public static uint DecodeUnitCode(ushort lowWord, ushort highWord)
        => CombineSwappedUnsigned32(lowWord, highWord);

    public static uint DecodeSwappedUnsigned32(ushort lowWord, ushort highWord)
        => CombineSwappedUnsigned32(lowWord, highWord);

    // ==================== Unit label normalization ====================

    public static string NormalizeUnitLabel(object? value)
    {
        if (value == null) return "";

        if (value is double d && double.IsFinite(d))
            return UnitCodeMap.TryGetValue((int)d, out var label) ? label : $"CODE_{(int)d}";

        var text = value.ToString()!.Trim();

        // Handle CODE_xxx prefix
        if (text.StartsWith("CODE_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text[5..], out var code) && UnitCodeMap.TryGetValue(code, out var codeLabel))
            return codeLabel;

        // Handle plain numeric
        if (int.TryParse(text, out var numCode) && UnitCodeMap.TryGetValue(numCode, out var numLabel))
            return numLabel;

        // Handle aliases
        if (UnitLabelAliasMap.TryGetValue(text, out var alias))
            return alias;

        return text;
    }

    // ==================== Decode helpers ====================

    public static string? DescribeAlarmCode(object? value)
    {
        if (value == null) return null;
        if (!int.TryParse(value.ToString(), out var alarmCode)) return null;
        return AlarmCodeMap.TryGetValue(alarmCode, out var desc) ? desc : null;
    }

    public static string DecodeResultCode(int? statusWord)
    {
        if (statusWord == null) return "UNKNOWN";
        var word = statusWord.Value;
        if ((word & (FlagFailTestPart | FlagFailReferencePart)) != 0) return "NG";
        if ((word & FlagPassPart) != 0) return "OK";
        return "UNKNOWN";
    }

    public static string? DecodeErrorCode(int? statusWord)
    {
        if (statusWord == null) return null;
        var word = statusWord.Value;
        if ((word & FlagPressureError) != 0) return "ATEQ_PRESSURE_ERROR";
        if ((word & FlagAlarm) != 0) return "ATEQ_ALARM";
        return null;
    }

    public static string? DescribeErrorCode(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode)) return null;
        var key = errorCode.Trim().ToUpperInvariant();
        return ErrorCodeTextMap.TryGetValue(key, out var desc) ? desc : null;
    }

    public static string? DescribeStatusWord(int? statusWord, string? resultCode)
    {
        if (statusWord == null) return null;
        var word = statusWord.Value;
        var reasons = new List<string>();
        if ((word & FlagFailTestPart) != 0) reasons.Add("测试件判定NG");
        if ((word & FlagFailReferencePart) != 0) reasons.Add("参考侧判定NG");
        if ((word & FlagPressureError) != 0) reasons.Add("压力异常");
        if ((word & FlagAlarm) != 0) reasons.Add("仪器报警");
        if (reasons.Count > 0) return string.Join("，", reasons);

        var rc = (resultCode ?? DecodeResultCode(word)).Trim().ToUpperInvariant();
        if (rc == "OK") return "测试合格";
        if ((word & FlagCycleEnd) != 0) return "测试循环结束";
        return null;
    }

    public static string? DeriveErrorText(int? statusWord, string? errorCode, string? resultCode)
        => DescribeStatusWord(statusWord, resultCode) ?? DescribeErrorCode(errorCode);

    // ==================== Legacy value normalization ====================

    private const double LegacySigned32ScaleOffset = 4294967.296;
    private const double ZeroPressureZeroLeakMarker = 9999;

    public static double? NormalizeLegacyLeakValue(double? value)
    {
        if (value == null || !double.IsFinite(value.Value)) return value;
        var v = value.Value;
        if (v <= -1000000 && v > -5000000)
        {
            var corrected = v + LegacySigned32ScaleOffset;
            if (Math.Abs(corrected) < 10000) return Math.Round(corrected, 6);
        }
        return v;
    }

    public static bool IsZeroMetricValue(double? value)
        => value != null && double.IsFinite(value.Value) && Math.Abs(value.Value) < 0.0000005;

    public static bool IsZeroOrMissingMetricValue(double? value)
        => value == null || IsZeroMetricValue(value);

    public static double? NormalizeFinalLeakValue(double? finalPressure, double? finalLeak)
    {
        if (IsZeroOrMissingMetricValue(finalPressure) && IsZeroMetricValue(finalLeak))
            return ZeroPressureZeroLeakMarker;
        return NormalizeLegacyLeakValue(finalLeak);
    }

    public static int? NormalizeTimingValue(double? value)
    {
        if (value == null || !double.IsFinite(value.Value)) return null;
        var v = value.Value;
        if (v < 0 || v > 600000) return null;
        return (int)Math.Round(v);
    }
}
