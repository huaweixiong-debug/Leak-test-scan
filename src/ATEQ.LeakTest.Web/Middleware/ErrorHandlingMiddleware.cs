using System.Text.Json;
using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Services;

namespace ATEQ.LeakTest.Web.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;

    public ErrorHandlingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[server] request failed: {ex.Message}");

            context.Response.ContentType = "application/json; charset=utf-8";

            if (ex is TestWorkflowException twe)
            {
                context.Response.StatusCode = twe.StatusCode;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = twe.Message,
                    error = twe.Cause?.Message
                }));
                return;
            }

            if (ex is ModbusException)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.InnerException?.Message
                }));
                return;
            }

            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = false,
                message = ex.Message,
                error = ex.InnerException?.Message
            }));
        }
    }
}
