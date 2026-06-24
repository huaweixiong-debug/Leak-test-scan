namespace ATEQ.LeakTest.Web.Models.Dto;

public class ProductProfilesRequest
{
    public List<ProductProfileDto> Products { get; set; } = new();
}

public class ProductProfileDto
{
    public string? Id { get; set; }
    public string ProductModel { get; set; } = string.Empty;
    public int AteqProgramNo { get; set; }
    public string QrKeyword { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
    public double? FillTime { get; set; }
    public double? StabTime { get; set; }
    public double? TestTime { get; set; }
    public bool? ScanConfirmEnabled { get; set; }
    public bool? ScanAutoStartEnabled { get; set; }
    public bool? ScanMatchEnabled { get; set; }
}
