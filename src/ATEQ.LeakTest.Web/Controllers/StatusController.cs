using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api")]
public class StatusController : ControllerBase
{
    private const int StatusStaleFallbackMs = 5000;

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            success = true,
            message = "ATEQ backend alive",
            build = "dotnet-1.0.0",
            monitor = new
            {
                defaultMonitorTimeoutMs = TestWorkflowService.DefaultMonitorTimeoutMs,
                maxMonitorSampleCount = TestWorkflowService.MaxMonitorSampleCount,
                activeSampleWindowCount = TestWorkflowService.ActiveSampleWindowCount,
                savedSampleWindowCount = TestWorkflowService.SavedSampleWindowCount
            }
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromServices] ModbusService modbus)
    {
        try
        {
            var status = await modbus.ReadRealtimeStatusAsync();
            return Ok(new
            {
                connected = status.Connected,
                enabled = status.Enabled,
                running = status.StepCode >= 4 && status.StepCode != 65535,
                currentJob = status.CurrentProgram,
                currentStep = status.StepCode,
                resultCode = status.ResultCode,
                errorCode = status.ErrorCode,
                errorText = status.ErrorText,
                stale = false,
                staleAgeMs = 0,
                degraded = false,
                telemetry = new
                {
                    pressure = status.Pressure,
                    pressureUnit = status.PressureUnit,
                    leak = status.Leak,
                    leakUnit = status.LeakUnit,
                    stepCode = status.StepCode,
                    statusWord = status.StatusWord
                }
            });
        }
        catch (Exception ex)
        {
            var cached = modbus.GetLastStatusSnapshot(StatusStaleFallbackMs);
            if (cached != null)
            {
                return Ok(new
                {
                    connected = true,
                    enabled = cached.Enabled,
                    running = cached.StepCode >= 4 && cached.StepCode != 65535,
                    currentJob = cached.CurrentProgram,
                    currentStep = cached.StepCode,
                    resultCode = cached.ResultCode,
                    errorCode = cached.ErrorCode,
                    errorText = cached.ErrorText,
                    stale = true,
                    staleAgeMs = cached.SnapshotAgeMs,
                    degraded = true,
                    errorDetail = ex.Message,
                    telemetry = new
                    {
                        pressure = cached.Pressure,
                        pressureUnit = cached.PressureUnit,
                        leak = cached.Leak,
                        leakUnit = cached.LeakUnit,
                        stepCode = cached.StepCode,
                        statusWord = cached.StatusWord
                    }
                });
            }

            var innerText = ex.InnerException?.Message;
            var errorDetail = string.IsNullOrEmpty(innerText) ? ex.Message : $"{ex.Message}: {innerText}";
            return StatusCode(503, new
            {
                connected = false,
                enabled = true,
                running = false,
                currentJob = (int?)null,
                currentStep = (int?)null,
                resultCode = "UNKNOWN",
                message = errorDetail,
                errorCode = ex.Message,
                errorDetail = errorDetail,
                errorText = errorDetail,
                stale = false,
                staleAgeMs = 0,
                degraded = false,
                telemetry = new
                {
                    pressure = (double?)null,
                    pressureUnit = "",
                    leak = (double?)null,
                    leakUnit = "",
                    stepCode = (int?)null,
                    statusWord = (int?)null
                }
            });
        }
    }
}
