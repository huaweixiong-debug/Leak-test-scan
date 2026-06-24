using System.Text;
using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Models.Dto;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api/tests")]
public class TestQueryController : ControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest([FromServices] DatabaseService db)
    {
        var records = await db.ListTestRecordsAsync();
        return Ok(new { success = true, total = records.Count, records = records.Take(50) });
    }

    [HttpGet("query")]
    public async Task<IActionResult> Query(
        [FromQuery] string? startTime,
        [FromQuery] string? endTime,
        [FromQuery] string? productModel,
        [FromQuery] string? resultCode,
        [FromQuery] string? qrCode,
        [FromQuery] string? failureReason,
        [FromQuery] bool? qrExact,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromServices] DatabaseService db = null!)
    {
        var filters = new TestQueryRequest
        {
            StartTime = startTime,
            EndTime = endTime,
            ProductModel = productModel,
            ResultCode = resultCode ?? "ALL",
            QrCode = qrCode,
            FailureReason = failureReason,
            QrExact = qrExact == true,
            Page = page,
            PageSize = pageSize
        };

        var result = await db.QueryTestRecordsAsync(filters);
        return Ok(new
        {
            success = true,
            filters = new
            {
                startTime = filters.StartTime,
                endTime = filters.EndTime,
                productModel = filters.ProductModel,
                resultCode = filters.ResultCode,
                qrCode = filters.QrCode,
                failureReason = filters.FailureReason,
                qrExact = filters.QrExact,
                page = filters.Page,
                pageSize = filters.PageSize
            },
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize,
            records = result.Records
        });
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string? startTime,
        [FromQuery] string? endTime,
        [FromQuery] string? productModel,
        [FromQuery] string? resultCode,
        [FromQuery] string? qrCode,
        [FromQuery] string? failureReason,
        [FromQuery] bool? qrExact,
        [FromServices] DatabaseService db = null!)
    {
        var filters = new TestQueryRequest
        {
            StartTime = startTime,
            EndTime = endTime,
            ProductModel = productModel,
            ResultCode = resultCode ?? "ALL",
            QrCode = qrCode,
            FailureReason = failureReason,
            QrExact = qrExact == true,
            DisablePaging = true
        };

        var result = await db.QueryTestRecordsAsync(filters);
        var csv = BuildTestsCsv(result.Records);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss");

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
            "text/csv; charset=utf-8", $"ateq-tests-{timestamp}.csv");
    }

    private static string BuildTestsCsv(List<Models.TestRecord> records)
    {
        var columns = new (string key, string label)[]
        {
            ("SequenceCode", "序号"), ("StartedAt", "测试时间"), ("QrCode", "二维码"),
            ("FinalPressure", "测试压力"), ("PressureUnit", "压力单位"),
            ("FinalLeak", "最终泄漏量"), ("LeakUnit", "泄漏单位"),
            ("ResultCode", "测试结果"), ("ErrorText", "失败原因"),
            ("ProductModel", "产品型号"), ("OperatorName", "操作人员"),
            ("StartMode", "启动方式"), ("AteqProgramNo", "ATEQ程序号"), ("ErrorCode", "错误码")
        };

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(c => EscapeCsv(c.label))));

        foreach (var r in records)
        {
            var values = columns.Select(c =>
            {
                var prop = typeof(Models.TestRecord).GetProperty(c.key);
                var val = prop?.GetValue(r);
                return EscapeCsv(val?.ToString());
            });
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? "";
        if (text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n'))
            return $"\"{text.Replace("\"", "\"\"")}\"";
        return text;
    }
}
