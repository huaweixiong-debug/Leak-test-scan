using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Models;
using ATEQ.LeakTest.Web.Models.Dto;
using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    [HttpGet("ateq")]
    public async Task<IActionResult> GetAteqConfig([FromServices] DatabaseService db)
    {
        var config = await db.GetCommConfigAsync("ateq");
        return Ok(new { success = true, config });
    }

    [HttpPost("ateq")]
    public async Task<IActionResult> SaveAteqConfig(
        [FromBody] CommConfigRequest body,
        [FromServices] DatabaseService db,
        [FromServices] ModbusService modbus,
        [FromServices] FeatureFlags flags)
    {
        var reject = CheckMockPort(body.ComPort, flags);
        if (reject != null) return reject;

        var config = NormalizeConfig(body, "ateq");
        var saved = await db.SaveCommConfigAsync("ateq", config);
        var state = await modbus.ConfigureAsync(saved);
        return Ok(new { success = true, config = saved, state });
    }

    [HttpGet("scanner")]
    public async Task<IActionResult> GetScannerConfig([FromServices] DatabaseService db)
    {
        var config = await db.GetCommConfigAsync("scanner");
        return Ok(new { success = true, config });
    }

    [HttpPost("scanner")]
    public async Task<IActionResult> SaveScannerConfig(
        [FromBody] CommConfigRequest body,
        [FromServices] DatabaseService db,
        [FromServices] ScannerService scanner,
        [FromServices] FeatureFlags flags)
    {
        var reject = CheckMockPort(body.ComPort, flags);
        if (reject != null) return reject;

        var config = NormalizeConfig(body, "scanner");
        var saved = await db.SaveCommConfigAsync("scanner", config);
        var state = await scanner.ConfigureAsync(saved);
        return Ok(new { success = true, config = saved, state });
    }

    private static IActionResult? CheckMockPort(string comPort, FeatureFlags flags)
    {
        if (!flags.EnableMockMode &&
            (string.Equals(comPort, "MOCK_ATEQ", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(comPort, "MOCK_SCANNER", StringComparison.OrdinalIgnoreCase)))
        {
            return new BadRequestObjectResult(new { success = false, message = "Mock mode is not enabled. Cannot use reserved mock port: " + comPort });
        }
        return null;
    }

    private static CommConfig NormalizeConfig(CommConfigRequest body, string deviceType)
    {
        return new CommConfig
        {
            ComPort = body.ComPort.ToUpperInvariant(),
            Baudrate = body.Baudrate,
            DataBits = body.DataBits,
            Parity = body.Parity.ToLowerInvariant(),
            StopBits = body.StopBits,
            SlaveId = deviceType == "ateq" ? body.SlaveId : null,
            TimeoutMs = body.TimeoutMs ?? 5000,
            PollIntervalMs = body.PollIntervalMs ?? 100,
            Dtr = NormalizeBool(body.Dtr, true),
            Rts = NormalizeBool(body.Rts, true),
            Enabled = body.Enabled
        };
    }

    internal static bool NormalizeBool(bool? value, bool defaultValue)
        => value ?? defaultValue;

    // ==================== PLC config ====================

    [HttpGet("plc")]
    public async Task<IActionResult> GetPlcConfig([FromServices] DatabaseService db)
    {
        var config = await db.GetPlcConfigAsync();
        return Ok(new { success = true, config });
    }

    [HttpPost("plc")]
    public async Task<IActionResult> SavePlcConfig(
        [FromBody] PlcConfigRequest body,
        [FromServices] DatabaseService db,
        [FromServices] PlcService plc,
        [FromServices] PlcCoordinatorService plcCoordinator)
    {
        var config = new PlcConfig
        {
            Enabled = body.Enabled,
            Host = body.Host,
            Port = body.Port,
            UnitId = body.UnitId,
            PollIntervalMs = body.PollIntervalMs ?? 250,
            StartAddressM1 = body.StartAddressM1 ?? 8193,
            OkAddressM2 = body.OkAddressM2 ?? 8194,
            NgAddressM3 = body.NgAddressM3 ?? 8195,
            ResetAddressM4 = body.ResetAddressM4 ?? 8196
        };
        var saved = await db.SavePlcConfigAsync(config);

        if (!saved.Enabled)
        {
            await plcCoordinator.StopAsync();
            plc.Disconnect();
            return Ok(new { success = true, config = saved, state = new { configured = true, connected = false, coordinatorRunning = false, reason = "PLC disabled" } });
        }

        // Enabled: await old loop exit, reconfigure, start new loop
        await plcCoordinator.StopAsync();
        var state = await plc.ConfigureAsync(saved);
        plcCoordinator.Start();

        return Ok(new { success = true, config = saved, state, coordinatorRunning = plcCoordinator.IsRunning });
    }
}
