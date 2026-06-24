namespace ATEQ.LeakTest.Web.Models;

public class TestRecord
{
    public string Id { get; set; } = string.Empty;
    public string BatchDate { get; set; } = string.Empty;
    public int DailySequence { get; set; }
    public string SequenceCode { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
    public string StartMode { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string ProductModel { get; set; } = string.Empty;
    public int AteqProgramNo { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public double? TestPressure { get; set; }
    public double? FinalPressure { get; set; }
    public string PressureUnit { get; set; } = string.Empty;
    public double? FinalLeak { get; set; }
    public string LeakUnit { get; set; } = string.Empty;
    public string ResultCode { get; set; } = "UNKNOWN";
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
    public int? RawStatusWord { get; set; }
    public int SampleCount { get; set; }
    public string Samples { get; set; } = "[]"; // JSON array
    public string? UpdatedAt { get; set; }
}
