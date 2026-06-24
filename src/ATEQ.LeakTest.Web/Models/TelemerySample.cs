namespace ATEQ.LeakTest.Web.Models;

public class TelemetrySample
{
    public string SampledAt { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public int StepCode { get; set; }
    public double Pressure { get; set; }
    public string PressureUnit { get; set; } = string.Empty;
    public double Leak { get; set; }
    public string LeakUnit { get; set; } = string.Empty;
    public string ResultCode { get; set; } = "UNKNOWN";
    public string? ErrorCode { get; set; }
    public int? StatusWord { get; set; }
}
