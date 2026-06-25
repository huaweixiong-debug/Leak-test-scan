using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Models;
using ATEQ.LeakTest.Web.Models.Dto;
using ATEQ.LeakTest.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ATEQ.LeakTest.Web.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts([FromServices] DatabaseService db)
    {
        var products = await db.ListProductProfilesAsync();
        return Ok(new { success = true, products });
    }

    [HttpPost("products")]
    public async Task<IActionResult> SaveProducts(
        [FromBody] ProductProfilesRequest body,
        [FromServices] DatabaseService db,
        [FromServices] TestWorkflowService workflow,
        [FromServices] ScannerService scanner)
    {
        var products = NormalizeProductProfiles(body);
        AssertUniqueBy(products, p => p.ProductModel, "productModel");
        AssertUniqueBy(products, p => p.QrKeyword, "qrKeyword");
        AssertUniqueBy(products, p => p.AteqProgramNo.ToString(), "ateqProgramNo");

        var saved = await db.SaveProductProfilesAsync(products);
        scanner.ClearLatestScan();
        await workflow.ReapplyM0ForCurrentSelectionAsync();
        return Ok(new { success = true, message = "Product profiles saved", products = saved });
    }

    [HttpGet("operators")]
    public async Task<IActionResult> GetOperators([FromServices] DatabaseService db)
    {
        var operators = await db.ListOperatorsAsync();
        return Ok(new { success = true, operators });
    }

    [HttpPost("operators")]
    public async Task<IActionResult> SaveOperators(
        [FromBody] OperatorsRequest body,
        [FromServices] DatabaseService db)
    {
        var operators = NormalizeOperators(body);
        AssertUniqueBy(operators, o => o.Name, "operator name");

        var saved = await db.SaveOperatorsAsync(operators);
        return Ok(new { success = true, message = "Operators saved", operators = saved });
    }

    private static List<ProductProfile> NormalizeProductProfiles(ProductProfilesRequest body)
    {
        return body.Products.Select((p, i) =>
        {
            var scanConfirmEnabled = ConfigController.NormalizeBool(p.ScanConfirmEnabled, true);
            var scanMatchEnabled = ConfigController.NormalizeBool(p.ScanMatchEnabled, false);
            var scanAutoStartEnabled = ConfigController.NormalizeBool(p.ScanAutoStartEnabled, false);

            if (scanAutoStartEnabled) scanConfirmEnabled = true;
            if (scanMatchEnabled) scanConfirmEnabled = true;

            return new ProductProfile
            {
                Id = p.Id ?? $"product-{i + 1}",
                ProductModel = p.ProductModel.Trim(),
                AteqProgramNo = p.AteqProgramNo,
                QrKeyword = p.QrKeyword.Trim(),
                IsActive = p.IsActive != false,
                FillTime = p.FillTime,
                StabTime = p.StabTime,
                TestTime = p.TestTime,
                ScanConfirmEnabled = scanConfirmEnabled,
                ScanMatchEnabled = scanMatchEnabled,
                ScanAutoStartEnabled = scanAutoStartEnabled
            };
        }).ToList();
    }

    private static List<Operator> NormalizeOperators(OperatorsRequest body)
    {
        return body.Operators.Select((o, i) => new Operator
        {
            Id = o.Id ?? $"operator-{i + 1}",
            Name = o.Name.Trim(),
            IsActive = o.IsActive != false
        }).ToList();
    }

    internal static void AssertUniqueBy<T>(List<T> items, Func<T, string> selector, string label)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var key = selector(item).Trim();
            if (!seen.Add(key))
                throw new InvalidOperationException($"{label} contains duplicate values");
        }
    }
}
