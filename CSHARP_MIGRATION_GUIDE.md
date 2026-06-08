# C# Migration Guide

本文件用于把当前 Node.js 项目迁移成 C# / ASP.NET Core 项目。推荐策略是：保留现有前端页面和 API 路径不变，只重写后端服务。这样迁移后前端几乎不用改，现场测试风险最低。

## 1. 迁移目标

目标：用 C# 实现与当前 Node.js 后端等价的服务。

保留：

- `public/*.html`
- `public/appearance.css`
- `public/appearance.js`
- 当前所有 `/api/...` 路径
- 当前 JSON 输入输出格式
- 当前业务规则：产品型号、程序号同步、扫码记录、扫码匹配、扫码启动、物理按键启动拦截、测试记录保存

替换：

- `server.js` -> ASP.NET Core Minimal API 或 Controllers
- `modbusService.js` -> C# `ModbusService`
- `scannerService.js` -> C# `ScannerService`
- `testWorkflowService.js` -> C# `TestWorkflowService`
- `db.js` -> C# SQLite Repository

## 2. 推荐技术栈

- .NET：`.NET 8 LTS`
- Web：`ASP.NET Core`
- 静态文件：`UseStaticFiles`
- 数据库：`Microsoft.Data.Sqlite`
- 串口：`System.IO.Ports`
- Modbus RTU：`NModbus`
- 后台轮询：`BackgroundService`
- 日志：内置 `Microsoft.Extensions.Logging`
- JSON：内置 `System.Text.Json`

推荐 NuGet 包：

```powershell
dotnet add package Microsoft.Data.Sqlite
dotnet add package NModbus
dotnet add package System.IO.Ports
```

## 3. 新项目结构建议

```text
LeakTestScan.CSharp/
├── Program.cs
├── appsettings.json
├── LeakTestScan.CSharp.csproj
├── Data/
│   ├── AppDb.cs
│   ├── Repositories/
│   │   ├── ConfigRepository.cs
│   │   ├── ProductRepository.cs
│   │   ├── OperatorRepository.cs
│   │   ├── ScannerEventRepository.cs
│   │   └── TestRecordRepository.cs
├── Models/
│   ├── CommConfig.cs
│   ├── ProductProfile.cs
│   ├── OperatorProfile.cs
│   ├── ScannerEvent.cs
│   ├── TestRecord.cs
│   ├── RealtimeStatus.cs
│   └── ApiResponses.cs
├── Services/
│   ├── ModbusService.cs
│   ├── ScannerService.cs
│   ├── TestWorkflowService.cs
│   ├── AteqObserverService.cs
│   └── CsvExportService.cs
├── wwwroot/
│   ├── index.html
│   ├── settings.html
│   ├── query.html
│   ├── comm-config.html
│   ├── scanner-test.html
│   ├── ateq-test.html
│   ├── appearance.css
│   └── appearance.js
└── data/
    └── leak-test.db
```

说明：

- 把当前 `public/` 内容复制到新项目 `wwwroot/`。
- C# 项目启动后直接访问 `http://127.0.0.1:3000/`。
- `data/` 不进 Git，用于本机配置和测试记录。

## 4. 当前 Node 模块到 C# 模块映射

| Node 文件 | C# 目标 | 说明 |
|---|---|---|
| `server.js` | `Program.cs` + API endpoints | 注册 API、静态文件、中间件、错误处理 |
| `db.js` | `AppDb.cs` + Repositories | SQLite 建表、CRUD、查询、CSV 数据来源 |
| `modbusService.js` | `ModbusService.cs` | RTU 连接、读状态、写程序号、启动、复位、读阶段时间 |
| `scannerService.js` | `ScannerService.cs` | 串口扫码枪连接、缓存最新扫码、DTR/RTS |
| `testWorkflowService.js` | `TestWorkflowService.cs` | 测试上下文、扫码规则、测试监控、保存记录 |
| `public/` | `wwwroot/` | 原样复制 |

## 5. API 对照表

C# 必须实现以下 API，并保持响应字段兼容。

| 方法 | 路径 | 用途 |
|---|---|---|
| `GET` | `/api/health` | 健康检查 |
| `GET` | `/api/config/ateq` | 获取仪器通讯配置 |
| `POST` | `/api/config/ateq` | 保存仪器通讯配置并重连 |
| `GET` | `/api/config/scanner` | 获取扫码枪通讯配置 |
| `POST` | `/api/config/scanner` | 保存扫码枪通讯配置并重连 |
| `GET` | `/api/settings/products` | 获取产品型号配置 |
| `POST` | `/api/settings/products` | 保存产品型号配置 |
| `GET` | `/api/settings/operators` | 获取操作员配置 |
| `POST` | `/api/settings/operators` | 保存操作员配置 |
| `GET` | `/api/scanner/latest` | 获取扫码枪连接状态和最新扫码 |
| `GET` | `/api/scanner/debug` | 获取扫码枪调试信息 |
| `POST` | `/api/scanner/debug/line-signals` | 设置 DTR/RTS 调试 |
| `GET` | `/api/status` | 获取实时仪器状态 |
| `GET` | `/api/program-timings?programNumber=1` | 读取程序阶段时间 |
| `GET` | `/api/test/active` | 获取当前测试流程状态 |
| `POST` | `/api/test/context` | 同步当前选择产品和程序号 |
| `POST` | `/api/start` | 软件启动测试 |
| `POST` | `/api/reset` | 复位/停止测试 |
| `GET` | `/api/tests/latest` | 最近测试记录 |
| `GET` | `/api/tests/query` | 条件查询测试记录 |
| `GET` | `/api/tests/export.csv` | 导出 CSV |

## 6. 关键 DTO 定义

### CommConfig

```csharp
public sealed class CommConfig
{
    public string DeviceType { get; set; } = "";
    public string ComPort { get; set; } = "";
    public int Baudrate { get; set; }
    public int DataBits { get; set; }
    public string Parity { get; set; } = "none";
    public double StopBits { get; set; }
    public int? SlaveId { get; set; }
    public int TimeoutMs { get; set; } = 5000;
    public int PollIntervalMs { get; set; } = 100;
    public bool Dtr { get; set; } = true;
    public bool Rts { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string? UpdatedAt { get; set; }
}
```

### ProductProfile

```csharp
public sealed class ProductProfile
{
    public string Id { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public int AteqProgramNo { get; set; }
    public string QrKeyword { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool ScanConfirmEnabled { get; set; } = true;
    public bool ScanMatchEnabled { get; set; } = false;
    public bool ScanAutoStartEnabled { get; set; } = false;
}
```

### RealtimeStatus

```csharp
public sealed class RealtimeStatus
{
    public bool Connected { get; set; }
    public bool Enabled { get; set; }
    public int StepCode { get; set; }
    public int StatusWord { get; set; }
    public int CurrentProgram { get; set; }
    public double Pressure { get; set; }
    public string PressureUnit { get; set; } = "";
    public double Leak { get; set; }
    public string LeakUnit { get; set; } = "";
    public string ResultCode { get; set; } = "UNKNOWN";
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
}
```

## 7. 数据库表

使用 SQLite。表结构应与当前项目兼容。

```sql
CREATE TABLE IF NOT EXISTS comm_configs (
  device_type TEXT PRIMARY KEY,
  com_port TEXT,
  baudrate INTEGER,
  data_bits INTEGER,
  parity TEXT,
  stop_bits REAL,
  slave_id INTEGER,
  timeout_ms INTEGER,
  poll_interval_ms INTEGER,
  dtr INTEGER,
  rts INTEGER,
  enabled INTEGER,
  updated_at TEXT
);

CREATE TABLE IF NOT EXISTS operators (
  id TEXT PRIMARY KEY,
  name TEXT,
  is_active INTEGER,
  updated_at TEXT
);

CREATE TABLE IF NOT EXISTS product_profiles (
  id TEXT PRIMARY KEY,
  product_model TEXT,
  ateq_program_no INTEGER,
  qr_keyword TEXT,
  is_active INTEGER,
  updated_at TEXT,
  fill_time REAL,
  stab_time REAL,
  test_time REAL,
  scan_confirm_enabled INTEGER,
  scan_auto_start_enabled INTEGER,
  scan_match_enabled INTEGER
);

CREATE TABLE IF NOT EXISTS scanner_events (
  id TEXT PRIMARY KEY,
  raw_text TEXT,
  scanned_at TEXT
);

CREATE TABLE IF NOT EXISTS test_records (
  id TEXT PRIMARY KEY,
  batch_date TEXT,
  daily_sequence INTEGER,
  sequence_code TEXT,
  started_at TEXT,
  finished_at TEXT,
  start_mode TEXT,
  qr_code TEXT,
  product_id TEXT,
  product_model TEXT,
  ateq_program_no INTEGER,
  operator_name TEXT,
  test_pressure REAL,
  final_pressure REAL,
  pressure_unit TEXT,
  final_leak REAL,
  leak_unit TEXT,
  result_code TEXT,
  error_code TEXT,
  raw_status_word INTEGER,
  sample_count INTEGER,
  samples TEXT,
  updated_at TEXT
);
```

## 8. Modbus 迁移重点

当前寄存器定义：

```text
WRITE_PROGRAM   = 0x0200
READ_PROGRAM    = 0x0202
EDIT_PROGRAM    = 0x3004
STEP_CODE       = 0x0020
REALTIME_STATUS = 0x0030
REALTIME_COUNT  = 13
RESET_COIL      = 0x0000
START_COIL      = 0x0001
```

C# 实现必须保留这些转换：

- 16 位寄存器需要 byte swap。
- 32 位数值由 lowWord + highWord 组合。
- 压力和漏率是 signed 32-bit，缩放 `/1000`。
- 漏率允许负数。
- 程序号写入时当前逻辑是 `programNumber - 1` 后 swap16。

核心函数：

```csharp
static ushort Swap16(ushort value)
{
    return (ushort)(((value & 0xff) << 8) | ((value >> 8) & 0xff));
}

static uint CombineSwappedUnsigned32(ushort lowWord, ushort highWord)
{
    uint low = Swap16(lowWord);
    uint high = Swap16(highWord);
    return (high << 16) + low;
}

static double DecodeSignedScaled32(ushort lowWord, ushort highWord)
{
    uint raw = CombineSwappedUnsigned32(lowWord, highWord);
    int signed = unchecked((int)raw);
    return signed / 1000.0;
}
```

单位映射必须包含：

```text
3000 -> mm3/s
51000 -> mL/min
11000 -> Bar
12000 -> kPa
```

注意：当前项目已经确认 `3000` 应显示为 `mm3/s`，不是 `mm3/h`。

## 9. 扫码枪迁移重点

Node 当前行为：

- 串口收到完整扫码后保存到 `scanner_events`。
- 只保留最近可见扫码。
- 测试完成保存记录后，清空当前扫码。
- 如果启用扫码自动启动，扫码后自动调用启动流程。
- 如果 ATEQ 不在空闲步骤 `65535`，忽略扫码。

C# 建议：

- `ScannerService` 使用 `SerialPort.DataReceived`。
- 使用缓冲区拼接数据。
- 支持 CR/LF 结束符。
- 保留 `LatestScan`。
- 保存 `scanner_events` 最近 200 条。
- 提供 `GetLatestVisibleScan()`。
- 提供 `ConsumeCurrentScan()`。

## 10. 测试流程迁移重点

这是迁移中最重要的模块。

必须保留以下状态：

- `activeRun`
- `pendingContext`
- `selectedContext`
- `lastObservedStepCode`
- `commandInFlight`
- `observeInFlight`
- `lastRejectedObservedRunAt`

必须保留以下规则：

1. 页面选择型号后，`POST /api/test/context` 立即选择仪器程序号。
2. 扫码自动启动时，优先使用页面已选择型号，不允许二维码全局匹配覆盖当前型号。
3. 第一次测试结束后，不能丢失当前选择型号，第二次扫码仍要用选中的型号。
4. 测试完成后保存记录并清空扫码结果。
5. 如果物理按键启动，但没有扫码或扫码不匹配，要立即发送复位/停止指令。
6. 如果轮询错过步骤 4，只要发现步骤在 `4..100`，也要恢复监控并保存记录。
7. 如果等待步骤 4 的上下文超过 8 秒并且仪器已空闲，可以释放 stale context。

物理按键启动拦截逻辑：

```text
observeTelemetry(status)
  if status.stepCode just entered 4 OR status.stepCode is between 4 and 100:
    resolveObservedContext(status)
      if product requires scan and no scan:
        throw rejected
      if product requires keyword match and QR mismatch:
        throw rejected
    if rejected:
      resetDevice()
      do not save record
```

## 11. 后台服务设计

建议两个后台服务：

### AteqObserverService

- 每 `500ms` 读取一次实时状态。
- 调用 `TestWorkflowService.ObserveTelemetry(status)`。
- 如果 Modbus 离线，只记录日志，不让服务崩溃。

### ScannerService

- 配置保存后重连串口。
- 串口收到扫码后调用 `HandleScannerInput`。
- `HandleScannerInput` 读取 ATEQ 状态，只有 step `65535` 时接受扫码。

## 12. ASP.NET Core Program.cs 骨架

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<ConfigRepository>();
builder.Services.AddSingleton<ProductRepository>();
builder.Services.AddSingleton<OperatorRepository>();
builder.Services.AddSingleton<ScannerEventRepository>();
builder.Services.AddSingleton<TestRecordRepository>();
builder.Services.AddSingleton<ModbusService>();
builder.Services.AddSingleton<ScannerService>();
builder.Services.AddSingleton<TestWorkflowService>();
builder.Services.AddHostedService<AteqObserverService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => new { success = true, message = "backend alive" });

// Add all API mappings here.

app.Run("http://0.0.0.0:3000");
```

## 13. 迁移顺序

推荐按以下顺序迁移，不要一口气全写完。

1. 创建 ASP.NET Core 项目，复制 `public/` 到 `wwwroot/`。
2. 实现 `/api/health`，确认页面能打开。
3. 实现 SQLite 建表和配置读写。
4. 实现 `/api/settings/products` 和 `/api/settings/operators`。
5. 实现 `/api/config/ateq` 和 `/api/config/scanner`。
6. 实现 `ModbusService.ReadRealtimeStatus()` 和 `/api/status`。
7. 实现程序号选择、启动、复位。
8. 实现 `ScannerService` 和 `/api/scanner/latest`。
9. 实现 `TestWorkflowService`。
10. 实现测试记录保存、查询、导出。
11. 做现场联调。

## 14. 验收清单

新 C# 项目完成后，至少验证这些场景：

- 页面能打开 `/`、`/settings.html`、`/comm-config.html`、`/query.html`。
- 保存通讯配置后仪器在线。
- 保存扫码枪配置后扫码枪在线。
- 切换产品型号后，仪器程序号同步变化。
- 型号 1/2/3 连续扫码启动时，第二次不会跳回程序 1。
- 扫码关键字不匹配时报警，不启动。
- 物理按键启动但未扫码时，仪器立即停止。
- 物理按键启动但扫码不匹配时，仪器立即停止。
- 测试完成后保存记录。
- 测试完成后扫码结果清空。
- 漏率趋势允许负数。
- 单位 `mm3/s` 显示正确。
- Light/System/Dark 外观可切换，Light 模式白底黑字清晰。
- 新电脑首次运行时自动创建 `data/`。

## 15. 不建议迁移的内容

不要照搬：

- `node_modules/`
- `runtime18/`
- `data/ateq.db`
- `data/runtime-store.json`
- 日志文件
- 临时测试请求 JSON

如果要迁移旧记录，可以单独写一次性导入工具，把旧 SQLite 或 JSON 数据导入新 C# SQLite。

## 16. 风险点

- Modbus 寄存器字节序必须严格对齐，否则压力、漏率、单位、程序号都会错。
- 串口事件是多线程回调，必须加锁保护扫码缓冲区和最新扫码。
- 测试流程不能并发启动，`TestWorkflowService` 里需要 `lock` 或 `SemaphoreSlim`。
- SQLite 写入需要串行化，避免测试完成和扫码保存同时写库冲突。
- 物理按键启动拦截不能无限发 reset，需要保留 2 秒节流。
- C# JSON 默认 PascalCase，要配置 camelCase，否则前端字段对不上。

## 17. 推荐完成标准

C# 迁移完成后，如果以下两条成立，可以认为后端替换成功：

- 不修改当前前端页面，所有功能都能正常使用。
- 与 Node 版本相比，API 响应字段和业务行为一致。
