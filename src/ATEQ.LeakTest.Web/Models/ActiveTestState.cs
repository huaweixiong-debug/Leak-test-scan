namespace ATEQ.LeakTest.Web.Models;

public class ActiveTestState
{
    public bool Running { get; set; }
    public string Stage { get; set; } = "idle";
    public string Message { get; set; } = string.Empty;
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public string? StartMode { get; set; }
    public string? QrCode { get; set; }
    public string? ScannerEventId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public MatchedProduct? MatchedProduct { get; set; }
    public TelemetrySample? LatestTelemetry { get; set; }
    public List<TelemetrySample> Samples { get; set; } = new();
    public string ResultCode { get; set; } = "UNKNOWN";
    public string? ErrorCode { get; set; }
    public SavedRecordInfo? SavedRecord { get; set; }
}

public class MatchedProduct
{
    public string Id { get; set; } = string.Empty;
    public string ProductModel { get; set; } = string.Empty;
    public int AteqProgramNo { get; set; }
    public string QrKeyword { get; set; } = string.Empty;
}

public class SavedRecordInfo
{
    public string Id { get; set; } = string.Empty;
    public string SequenceCode { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
    public string ProductModel { get; set; } = string.Empty;
    public string ResultCode { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
    public double? FinalPressure { get; set; }
    public string PressureUnit { get; set; } = string.Empty;
    public double? FinalLeak { get; set; }
    public string LeakUnit { get; set; } = string.Empty;
}
