namespace ATEQ.LeakTest.Web.Models.Dto;

public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message)
        => new() { Success = false, Message = message };
}

public class ApiResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public object? Data { get; set; }

    public static ApiResponse Ok(object? data = null, string? message = null)
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse Fail(string message, object? data = null)
        => new() { Success = false, Message = message, Data = data };
}
