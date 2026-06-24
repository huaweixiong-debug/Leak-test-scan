namespace ATEQ.LeakTest.Web.Models;

/// <summary>
/// Strong-typed snapshot of the 4 PLC I/O coils.
/// Populated from configured addresses, not hardcoded 8192-8196.
/// </summary>
public sealed class PlcIoSnapshot
{
    public bool M1 { get; set; }
    public bool M2 { get; set; }
    public bool M3 { get; set; }
    public bool M4 { get; set; }

    /// <summary>All 4 values in M1..M4 order, useful for the test page values[] array.</summary>
    public bool[] All => [M1, M2, M3, M4];
}
