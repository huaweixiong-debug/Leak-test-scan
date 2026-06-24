namespace ATEQ.LeakTest.Web.Models.Dto;

public class OperatorsRequest
{
    public List<OperatorDto> Operators { get; set; } = new();
}

public class OperatorDto
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}
