namespace ATEQ.LeakTest.Web.Models.Dto;

public class CommConfigRequest
{
    public string ComPort { get; set; } = string.Empty;
    public int Baudrate { get; set; }
    public int DataBits { get; set; }
    public string Parity { get; set; } = "none";
    public double StopBits { get; set; }
    public int? SlaveId { get; set; }
    public int? TimeoutMs { get; set; }
    public int? PollIntervalMs { get; set; }
    public bool? Dtr { get; set; }
    public bool? Rts { get; set; }
    public bool Enabled { get; set; }
}
