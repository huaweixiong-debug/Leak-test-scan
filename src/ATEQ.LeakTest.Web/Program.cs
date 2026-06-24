using ATEQ.LeakTest.Web.Data;
using ATEQ.LeakTest.Web.Infrastructure;
using ATEQ.LeakTest.Web.Middleware;
using ATEQ.LeakTest.Web.Models;
using ATEQ.LeakTest.Web.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var storagePaths = StoragePaths.Resolve(
    builder.Environment.ContentRootPath,
    AppContext.BaseDirectory,
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=data/ateq.db");

storagePaths.EnsurePrimaryStorageReady();

// Feature flags
var featureFlags = builder.Configuration.GetSection(FeatureFlags.SectionName).Get<FeatureFlags>() ?? new FeatureFlags();
builder.Services.AddSingleton(featureFlags);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(storagePaths.ConnectionString));

builder.Services.AddSingleton(storagePaths);
builder.Services.AddSingleton<ModbusService>();
builder.Services.AddSingleton<ScannerService>();
builder.Services.AddSingleton<PlcService>();
builder.Services.AddSingleton<PlcCoordinatorService>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddSingleton<TestWorkflowService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
var staticRootPath = ResolveStaticRootPath(builder.Environment.ContentRootPath, AppContext.BaseDirectory);
var staticFileProvider = new PhysicalFileProvider(staticRootPath);

// Boot: init DB, apply configs, wire scanner handler, start observer
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<DatabaseService>();
    var modbus = services.GetRequiredService<ModbusService>();
    var scanner = services.GetRequiredService<ScannerService>();
    var workflow = services.GetRequiredService<TestWorkflowService>();
    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

    await db.InitializeAsync();

    // Scanner input handler (equivalent to handleScannerInput in server.js)
    scanner.OnScan = async (scanEvent) =>
    {
        using var handlerScope = scopeFactory.CreateScope();
        var handlerDb = handlerScope.ServiceProvider.GetRequiredService<DatabaseService>();
        var handlerModbus = handlerScope.ServiceProvider.GetRequiredService<ModbusService>();
        var handlerScanner = handlerScope.ServiceProvider.GetRequiredService<ScannerService>();
        var handlerWorkflow = handlerScope.ServiceProvider.GetRequiredService<TestWorkflowService>();

        try
        {
            // Persist scan unconditionally before any workflow gating.
            var savedScan = await handlerDb.SaveScannerEventAsync(scanEvent.RawText);
            handlerScanner.SyncLatestScan(savedScan);

            // ATEQ status check: only gate auto-start, not persistence.
            try
            {
                var status = await handlerModbus.ReadRealtimeStatusAsync();
                if (status.StepCode != 65535)
                {
                    Console.WriteLine($"[scanner] scan saved (id={savedScan.Id}) but ATEQ step={status.StepCode} (not idle) — auto-start skipped");
                    return;
                }
                await handlerWorkflow.MaybeAutoStartFromScanAsync(savedScan);
            }
            catch (ModbusException mex)
            {
                Console.Error.WriteLine($"[scanner] scan saved (id={savedScan.Id}) but ATEQ unavailable — auto-start skipped: {mex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[scanner] failed to persist scan: {ex.Message}");
        }
    };

    // Apply boot configurations
    var ateqConfig = await db.GetCommConfigAsync("ateq");
    var scannerConfig = await db.GetCommConfigAsync("scanner");

    if (ateqConfig != null)
    {
        try { await modbus.ConfigureAsync(ateqConfig); }
        catch (Exception ex) { Console.WriteLine($"[boot] failed to apply ateq config: {ex.Message}"); }
    }

    if (scannerConfig != null)
    {
        try { await scanner.ConfigureAsync(scannerConfig); }
        catch (Exception ex) { Console.WriteLine($"[boot] failed to apply scanner config: {ex.Message}"); }
    }

    // PLC coordinator boot
    var plc = services.GetRequiredService<PlcService>();
    var plcCoordinator = services.GetRequiredService<PlcCoordinatorService>();
    var plcConfig = await db.GetPlcConfigAsync();
    if (plcConfig != null)
    {
        try { await plc.ConfigureAsync(plcConfig); }
        catch (Exception ex) { Console.WriteLine($"[boot] failed to apply PLC config: {ex.Message}"); }
    }
    if (plcConfig?.Enabled == true)
    {
        plcCoordinator.Start();
        Console.WriteLine("[boot] PLC coordinator started");
    }

    // ATEQ status observer loop (equivalent to observeAteqState in server.js)
    _ = Task.Run(async () =>
    {
        var observerInterval = ateqConfig?.PollIntervalMs > 0 ? ateqConfig.PollIntervalMs : 500;
        while (true)
        {
            try
            {
                await Task.Delay(observerInterval);
                if (workflow.ShouldObserveTelemetry())
                {
                    var status = await modbus.ReadRealtimeStatusAsync();
                    await workflow.ObserveTelemetryAsync(status);
                }
            }
            catch (Exception ex) when (ex is not ModbusException)
            {
                Console.Error.WriteLine($"[observer] loop failed: {ex.Message}");
            }
            catch (ModbusException)
            {
                // Silently ignore Modbus errors in observer loop
            }
        }
    });
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();

// Rewrite extensionless paths to .html for static pages (e.g. /index -> /index.html)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (!string.IsNullOrEmpty(path) && !path.StartsWith("/api/") && !Path.HasExtension(path))
    {
        var htmlPath = path.TrimEnd('/') + ".html";
        var filePath = Path.Combine(staticRootPath, htmlPath.TrimStart('/'));
        if (File.Exists(filePath))
        {
            context.Request.Path = htmlPath;
        }
    }
    await next();
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = staticFileProvider
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = staticFileProvider
});
app.MapControllers();

app.Run();

static string ResolveStaticRootPath(string contentRootPath, string appBaseDirectory)
{
    var candidates = new[]
    {
        Path.Combine(contentRootPath, "wwwroot"),
        Path.Combine(contentRootPath, "src", "ATEQ.LeakTest.Web", "wwwroot"),
        Path.Combine(appBaseDirectory, "wwwroot"),
        Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", "wwwroot")),
        Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", "src", "ATEQ.LeakTest.Web", "wwwroot"))
    };

    foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (Directory.Exists(candidate))
            return candidate;
    }

    return Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot"));
}
