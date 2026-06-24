namespace ATEQ.LeakTest.Web.Models.Dto;

public class TestQueryRequest
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? ProductModel { get; set; }
    public string? ResultCode { get; set; } = "ALL";
    public string? QrCode { get; set; }
    public string? FailureReason { get; set; }
    public bool? QrExact { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool DisablePaging { get; set; }
}

public class TestQueryResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<TestRecord> Records { get; set; } = new();
}
