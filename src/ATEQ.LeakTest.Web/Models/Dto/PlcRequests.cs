namespace ATEQ.LeakTest.Web.Models.Dto;

public class PlcConnectRequest
{
    public string Host { get; set; } = "192.168.2.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
}

public class PlcWriteCoilRequest
{
    public string Label { get; set; } = "";
    public bool Value { get; set; }
}

public class PlcConfigRequest
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "192.168.2.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public int? PollIntervalMs { get; set; }
    public ushort? StartAddressM1 { get; set; }
    public ushort? OkAddressM2 { get; set; }
    public ushort? NgAddressM3 { get; set; }
    public ushort? ResetAddressM4 { get; set; }
}
