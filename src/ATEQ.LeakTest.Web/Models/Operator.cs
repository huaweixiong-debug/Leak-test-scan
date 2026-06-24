namespace ATEQ.LeakTest.Web.Models;

public class Operator
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? UpdatedAt { get; set; }
}
