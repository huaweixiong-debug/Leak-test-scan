using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Models.Dto;
using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api/plc")]
public class PlcTestController : ControllerBase
{
    [HttpPost("connect")]
    public async Task<IActionResult> Connect(
        [FromBody] PlcConnectRequest body,
        [FromServices] PlcService plc)
    {
        if (string.IsNullOrWhiteSpace(body.Host))
            return BadRequest(new { success = false, message = "Host is required" });
        if (body.Port < 1 || body.Port > 65535)
            return BadRequest(new { success = false, message = "Port must be 1-65535" });

        try
        {
            var result = await plc.ConnectAsync(body.Host, body.Port, body.UnitId);
            return Ok(new { success = true, data = result });
        }
        catch (ModbusException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("disconnect")]
    public IActionResult Disconnect([FromServices] PlcService plc)
    {
        plc.Disconnect();
        return Ok(new { success = true, message = "Disconnected" });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(
        [FromServices] PlcService plc,
        [FromServices] PlcCoordinatorService plcCoordinator,
        [FromServices] DatabaseService db)
    {
        var plcConfig = await db.GetPlcConfigAsync();
        var enabled = plcConfig?.Enabled == true;

        try
        {
            var plsStatus = await plc.GetStatusAsync();
            return Ok(new
            {
                success = true,
                data = new
                {
                    coordinatorRunning = plcCoordinator.IsRunning,
                    online = plcCoordinator.Online,
                    lastError = (string?)null,
                    lastPollAt = plcCoordinator.LastPollAt,
                    enabled,
                    stale = false,
                    staleAgeMs = 0,
                    degraded = false,
                    plsStatus
                }
            });
        }
        catch (ModbusException ex)
        {
            var lastError = ex.Message;
            var cached = plc.GetLastStatusSnapshot();
            if (cached != null)
            {
                var staleAgeMs = cached.AgeMs;
                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        coordinatorRunning = plcCoordinator.IsRunning,
                        online = true,
                        lastError,
                        lastPollAt = plcCoordinator.LastPollAt,
                        enabled,
                        stale = true,
                        staleAgeMs,
                        degraded = true,
                        plsStatus = cached.Data
                    }
                });
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    coordinatorRunning = plcCoordinator.IsRunning,
                    online = false,
                    lastError,
                    lastPollAt = (DateTime?)null,
                    enabled,
                    stale = false,
                    staleAgeMs = 0,
                    degraded = false,
                    connected = false
                }
            });
        }
    }

    [HttpGet("read-map")]
    public async Task<IActionResult> ReadMap([FromServices] PlcService plc)
    {
        try
        {
            var result = await plc.ReadMapAsync();
            return Ok(new { success = true, data = result });
        }
        catch (ModbusException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("manual-monitor")]
    public async Task<IActionResult> ManualMonitor(
        [FromServices] PlcService plc,
        [FromServices] PlcCoordinatorService plcCoordinator,
        [FromServices] DatabaseService db)
    {
        var plcConfig = await db.GetPlcConfigAsync();
        var enabled = plcConfig?.Enabled == true;

        try
        {
            var panel = await plc.ReadManualMonitorAsync();
            return Ok(new
            {
                success = true,
                data = new
                {
                    coordinatorRunning = plcCoordinator.IsRunning,
                    online = true,
                    lastError = (string?)null,
                    lastPollAt = plcCoordinator.LastPollAt,
                    enabled,
                    stale = false,
                    staleAgeMs = 0,
                    degraded = false,
                    connection = new
                    {
                        host = plc.Host,
                        port = plc.Port,
                        unitId = plc.UnitId
                    },
                    panel
                }
            });
        }
        catch (ModbusException ex)
        {
            var cached = plc.GetLastManualMonitorSnapshot();
            if (cached != null)
            {
                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        coordinatorRunning = plcCoordinator.IsRunning,
                        online = true,
                        lastError = ex.Message,
                        lastPollAt = plcCoordinator.LastPollAt,
                        enabled,
                        stale = true,
                        staleAgeMs = cached.AgeMs,
                        degraded = true,
                        connection = new
                        {
                            host = plcConfig?.Host ?? plc.Host,
                            port = plcConfig?.Port ?? plc.Port,
                            unitId = plcConfig?.UnitId ?? plc.UnitId
                        },
                        panel = cached.Data
                    }
                });
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    coordinatorRunning = plcCoordinator.IsRunning,
                    online = false,
                    lastError = ex.Message,
                    lastPollAt = plcCoordinator.LastPollAt,
                    enabled,
                    stale = false,
                    staleAgeMs = 0,
                    degraded = false,
                    connection = new
                    {
                        host = plcConfig?.Host ?? plc.Host,
                        port = plcConfig?.Port ?? plc.Port,
                        unitId = plcConfig?.UnitId ?? plc.UnitId
                    },
                    panel = new
                    {
                        connected = false,
                        host = plcConfig?.Host ?? plc.Host,
                        port = plcConfig?.Port ?? plc.Port,
                        unitId = plcConfig?.UnitId ?? plc.UnitId,
                        controls = Array.Empty<object>(),
                        alarms = Array.Empty<object>(),
                        inputs = Array.Empty<object>()
                    }
                }
            });
        }
    }

    [HttpPost("write-coil")]
    public async Task<IActionResult> WriteCoil(
        [FromBody] PlcWriteCoilRequest body,
        [FromServices] PlcService plc)
    {
        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { success = false, message = "Label is required (M0-M4)" });

        try
        {
            var result = await plc.WriteCoilAsync(body.Label, body.Value);
            return Ok(new { success = true, data = result });
        }
        catch (ModbusException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("reset-outputs")]
    public async Task<IActionResult> ResetOutputs([FromServices] PlcService plc)
    {
        try
        {
            var result = await plc.ResetOutputsAsync();
            return Ok(new { success = true, data = result });
        }
        catch (ModbusException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("manual-reset")]
    public async Task<IActionResult> ManualReset([FromServices] PlcService plc)
    {
        try
        {
            var result = await plc.ResetManualOutputsAsync();
            return Ok(new { success = true, data = result });
        }
        catch (ModbusException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
