using ATEQ.LeakTest.Web.Models;
using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

/// <summary>
/// Mock debugging endpoints. Only reachable when EnableMockMode = true.
/// </summary>
[ApiController]
[Route("api/debug/mock")]
public class DebugMockController : ControllerBase
{
    /// <summary>Control the next mock ATEQ test result.</summary>
    [HttpPost("ateq/next-result")]
    public IActionResult SetMockAteqResult(
        [FromBody] MockResultRequest body,
        [FromServices] ModbusService modbus,
        [FromServices] FeatureFlags flags)
    {
        if (!flags.EnableMockMode)
            return NotFound(new { success = false, message = "Mock mode is not enabled" });

        var resultCode = body.ResultCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(resultCode) || (resultCode != "OK" && resultCode != "NG"))
            return BadRequest(new { success = false, message = "resultCode must be OK or NG" });

        modbus.MockNextResult = resultCode;
        modbus.MockNextError = body.ErrorCode ?? "";

        return Ok(new
        {
            success = true,
            message = $"Next mock result set to {resultCode}" + (string.IsNullOrEmpty(body.ErrorCode) ? "" : $" with error {body.ErrorCode}"),
            resultCode,
            errorCode = body.ErrorCode
        });
    }
}

public class MockResultRequest
{
    public string ResultCode { get; set; } = "OK";
    public string? ErrorCode { get; set; }
}
