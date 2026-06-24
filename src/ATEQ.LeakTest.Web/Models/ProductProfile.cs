namespace ATEQ.LeakTest.Web.Models;

public class ProductProfile
{
    public string Id { get; set; } = string.Empty;
    public string ProductModel { get; set; } = string.Empty;
    public int AteqProgramNo { get; set; }
    public string QrKeyword { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? UpdatedAt { get; set; }
    public double? FillTime { get; set; }
    public double? StabTime { get; set; }
    public double? TestTime { get; set; }
    public bool ScanConfirmEnabled { get; set; } = true;
    public bool ScanAutoStartEnabled { get; set; }
    public bool ScanMatchEnabled { get; set; }
}
