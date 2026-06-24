using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api/program-timings")]
public class ProgramTimingsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTimings(
        [FromQuery] int programNumber,
        [FromServices] ModbusService modbus)
    {
        if (programNumber < 1 || programNumber > 255)
            return BadRequest(new { success = false, message = "programNumber must be between 1 and 255" });

        var timings = await modbus.ReadProgramTimingsAsync(programNumber);
        return Ok(new { success = true, timings });
    }
}
