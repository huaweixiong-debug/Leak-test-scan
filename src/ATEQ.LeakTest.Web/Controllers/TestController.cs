using ATEQ.LeakTest.Web.Models;
using ATEQ.LeakTest.Web.Models.Dto;
using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
public class TestController : ControllerBase
{
    [HttpGet("api/test/active")]
    public IActionResult GetActive([FromServices] TestWorkflowService workflow)
    {
        return Ok(new { success = true, activeTest = workflow.GetActiveState() });
    }

    [HttpPost("api/test/context")]
    public async Task<IActionResult> SyncContext(
        [FromBody] ContextRequest body,
        [FromServices] TestWorkflowService workflow)
    {
        var result = await workflow.SyncContextAsync(new ContextPayload
        {
            ProductModel = body.ProductModel,
            OperatorName = body.OperatorName
        });
        return Ok(result);
    }

    [HttpPost("api/start")]
    public async Task<IActionResult> Start(
        [FromBody] StartRequest body,
        [FromServices] TestWorkflowService workflow)
    {
        var result = await workflow.StartAsync(new StartPayload
        {
            ProductModel = body.ProductModel,
            OperatorName = body.OperatorName,
            QrCode = body.QrCode,
            SkipProgramSelect = body.SkipProgramSelect,
            StartMode = body.StartMode ?? (!string.IsNullOrEmpty(body.QrCode) ? "scan" : "manual")
        });
        return Ok(result);
    }

    [HttpPost("api/reset")]
    public async Task<IActionResult> Reset(
        [FromServices] ModbusService modbus,
        [FromServices] TestWorkflowService workflow)
    {
        await modbus.ResetDeviceAsync();
        workflow.HandleResetCommand();

        RealtimeStatus? status = null;
        try { status = await modbus.ReadRealtimeStatusAsync(); } catch { /* ignore */ }

        return Ok(new
        {
            success = true,
            message = "ATEQ reset command sent",
            resultCode = status?.ResultCode ?? "UNKNOWN",
            errorCode = (string?)null,
            activeTest = workflow.GetActiveState()
        });
    }
}
