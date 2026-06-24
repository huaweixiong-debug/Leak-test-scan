using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Models.Dto;
using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api/scanner")]
public class ScannerController : ControllerBase
{
    [HttpGet("latest")]
    public IActionResult GetLatest([FromServices] ScannerService scanner)
    {
        return Ok(new
        {
            success = true,
            connected = scanner.IsConnected,
            latestScan = scanner.GetLatestVisibleScan()
        });
    }

    [HttpGet("debug")]
    public IActionResult GetDebug([FromServices] ScannerService scanner)
    {
        return Ok(new
        {
            success = true,
            data = scanner.GetDebugState()
        });
    }

    [HttpPost("debug/line-signals")]
    public async Task<IActionResult> UpdateLineSignals(
        [FromBody] LineSignalRequest body,
        [FromServices] DatabaseService db,
        [FromServices] ScannerService scanner)
    {
        var currentConfig = await db.GetCommConfigAsync("scanner")
            ?? throw new InvalidOperationException("Scanner config not found");
        if (currentConfig == null) return NotFound(new { success = false, message = "Scanner config not found" });

        currentConfig.Dtr = ConfigController.NormalizeBool(body.Dtr, true);
        currentConfig.Rts = ConfigController.NormalizeBool(body.Rts, true);
        var savedConfig = await db.SaveCommConfigAsync("scanner", currentConfig);

        var state = await scanner.UpdateLineSignalsAsync(new
        {
            Dtr = savedConfig.Dtr,
            Rts = savedConfig.Rts,
            Reconnect = ConfigController.NormalizeBool(body.Reconnect, true)
        });

        return Ok(new
        {
            success = true,
            message = "Scanner line signals updated",
            config = savedConfig,
            state
        });
    }

    /// <summary>Production keyboard-wedge scanner input. Only accepted when scanner is in KEYBOARD_WEDGE mode.</summary>
    [HttpPost("input")]
    public IActionResult ScannerInput(
        [FromBody] ScanInputRequest body,
        [FromServices] ScannerService scanner,
        [FromServices] Data.DatabaseService db)
    {
        if (!scanner.IsKeyboardWedge)
            return BadRequest(new { success = false, message = "Scanner is not in KEYBOARD_WEDGE mode. Configure scanner first." });

        if (string.IsNullOrWhiteSpace(body.RawText))
            return BadRequest(new { success = false, message = "rawText is required" });

        var scanEvent = scanner.AcceptScan(body.RawText);
        scanner.SyncLatestScan(scanEvent);

        return Ok(new { success = true, message = "Scan received", scanEvent });
    }

    /// <summary>Mock endpoint: inject a scan event without physical hardware.</summary>
    [HttpPost("debug/mock-scan")]
    public IActionResult InjectMockScan(
        [FromBody] ScanInputRequest body,
        [FromServices] ScannerService scanner,
        [FromServices] Data.DatabaseService db,
        [FromServices] Models.FeatureFlags flags)
    {
        if (!flags.EnableMockMode)
            return NotFound(new { success = false, message = "Mock mode is not enabled" });

        if (string.IsNullOrWhiteSpace(body.RawText))
            return BadRequest(new { success = false, message = "rawText is required" });

        var scanEvent = scanner.AcceptScan(body.RawText);
        scanner.SyncLatestScan(scanEvent);

        return Ok(new
        {
            success = true,
            message = "Mock scan injected",
            scanEvent
        });
    }

    /// <summary>Recent scan history from scanner_events table.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int take = 20,
        [FromServices] Data.DatabaseService db = null!)
    {
        var events = await db.ListScannerEventsAsync(take);
        var latest = events.FirstOrDefault();
        return Ok(new { success = true, total = events.Count, latestScan = latest, history = events });
    }
}

public class ScanInputRequest
{
    public string RawText { get; set; } = string.Empty;
}
