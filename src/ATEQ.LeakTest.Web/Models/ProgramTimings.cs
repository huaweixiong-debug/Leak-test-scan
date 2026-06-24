namespace ATEQ.LeakTest.Web.Models;

public class ProgramTimings
{
    public int ProgramNumber { get; set; }
    public string Source { get; set; } = string.Empty;
    public int? FillTimeMs { get; set; }
    public int? StabTimeMs { get; set; }
    public int? TestTimeMs { get; set; }
    public int? DumpTimeMs { get; set; }
    public int? TotalTimeMs { get; set; }
    public double? FillTimeSeconds { get; set; }
    public double? StabTimeSeconds { get; set; }
    public double? TestTimeSeconds { get; set; }
    public double? DumpTimeSeconds { get; set; }
    public double? TotalTimeSeconds { get; set; }
    public ProgramTimingsDiagnostics? Diagnostics { get; set; }
}

public class ProgramTimingsDiagnostics
{
    public string? PrimarySource { get; set; }
    public string? FallbackSource { get; set; }
    public FallbackTimings? FallbackTimings { get; set; }
}

public class FallbackTimings
{
    public int? FillTimeMs { get; set; }
    public int? StabTimeMs { get; set; }
    public int? TestTimeMs { get; set; }
    public int? DumpTimeMs { get; set; }
}
