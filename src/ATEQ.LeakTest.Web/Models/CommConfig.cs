namespace ATEQ.LeakTest.Web.Models;

public class CommConfig
{
    public int Id { get; set; }
    public string DeviceType { get; set; } = string.Empty; // "ateq" or "scanner"
    public string ComPort { get; set; } = "COM1";
    public int Baudrate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "none";
    public double StopBits { get; set; } = 1;
    public int? SlaveId { get; set; }
    public int TimeoutMs { get; set; } = 5000;
    public int PollIntervalMs { get; set; } = 100;
    public bool Dtr { get; set; } = true;
    public bool Rts { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string? UpdatedAt { get; set; }
}
