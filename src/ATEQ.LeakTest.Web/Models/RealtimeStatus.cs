namespace ATEQ.LeakTest.Web.Models;

public class RealtimeStatus
{
    public bool Connected { get; set; }
    public bool Enabled { get; set; } = true;
    public int StepCode { get; set; }
    public int StatusWord { get; set; }
    public int CurrentProgram { get; set; }
    public double Pressure { get; set; }
    public string PressureUnit { get; set; } = string.Empty;
    public double Leak { get; set; }
    public string LeakUnit { get; set; } = string.Empty;
    public string ResultCode { get; set; } = "UNKNOWN";
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
    public long SnapshotAgeMs { get; set; }
}
