namespace ATEQ.LeakTest.Web.Models;

/// <summary>
/// PLC Modbus TCP configuration — single-row table, keyed by Id="plc".
/// Separate from CommConfig (which is for serial-port devices).
/// </summary>
public class PlcConfig
{
    public string Id { get; set; } = "plc";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "192.168.2.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public int PollIntervalMs { get; set; } = 250;
    public ushort StartAddressM1 { get; set; } = 8193;
    public ushort OkAddressM2 { get; set; } = 8194;
    public ushort NgAddressM3 { get; set; } = 8195;
    public ushort ResetAddressM4 { get; set; } = 8196;
    public string? UpdatedAt { get; set; }
}
