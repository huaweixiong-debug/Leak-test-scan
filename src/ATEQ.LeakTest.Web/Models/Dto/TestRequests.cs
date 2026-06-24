namespace ATEQ.LeakTest.Web.Models.Dto;

public class StartRequest
{
    public string? ProductModel { get; set; }
    public string? OperatorName { get; set; }
    public string? QrCode { get; set; }
    public string? ScannerEventId { get; set; }
    public bool? SkipProgramSelect { get; set; }
    public string? StartMode { get; set; }
}

public class ContextRequest
{
    public string ProductModel { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
}

public class LineSignalRequest
{
    public bool Dtr { get; set; }
    public bool Rts { get; set; }
    public bool? Reconnect { get; set; }
}
