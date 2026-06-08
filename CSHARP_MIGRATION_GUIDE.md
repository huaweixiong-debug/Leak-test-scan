# C# Migration Guide

This document describes how to migrate the current Node.js leak-test scan project to C# / ASP.NET Core.

The safest migration strategy is:

- Keep the current frontend files.
- Keep the current `/api/...` routes and JSON shapes.
- Replace only the backend implementation.

This keeps the browser UI almost unchanged and makes field validation much easier.

## 1. Migration Goal

Build a C# backend that behaves the same as the current Node.js backend.

Keep:

- `public/*.html`
- `public/appearance.css`
- `public/appearance.js`
- all current `/api/...` routes
- all current JSON request and response fields
- product model rules
- program number synchronization
- scan recording
- scan keyword matching
- scan auto-start
- physical-button start rejection
- test record saving

Replace:

- `server.js` -> ASP.NET Core Minimal API or Controllers
- `db.js` -> SQLite repository layer
- `modbusService.js` -> C# `ModbusService`
- `scannerService.js` -> C# `ScannerService`
- `testWorkflowService.js` -> C# `TestWorkflowService`

## 2. Recommended Technology Stack

- Runtime: `.NET 8 LTS`
- Web framework: `ASP.NET Core`
- Static files: `UseStaticFiles`
- Database: `Microsoft.Data.Sqlite`
- Serial port: `System.IO.Ports`
- Modbus RTU: `NModbus`
- Background polling: `BackgroundService`
- Logging: built-in `Microsoft.Extensions.Logging`
- JSON: built-in `System.Text.Json`

Recommended NuGet packages:

```powershell
dotnet add package Microsoft.Data.Sqlite
dotnet add package NModbus
dotnet add package System.IO.Ports
```

## 3. Suggested Project Structure

```text
LeakTestScan.CSharp/
├── Program.cs
├── appsettings.json
├── LeakTestScan.CSharp.csproj
├── Data/
│   ├── AppDb.cs
│   └── Repositories/
│       ├── ConfigRepository.cs
│       ├── ProductRepository.cs
│       ├── OperatorRepository.cs
│       ├── ScannerEventRepository.cs
│       └── TestRecordRepository.cs
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

Copy the current `public/` folder into `wwwroot/`.

The C# app should listen on:

```text
http://127.0.0.1:3000
```

## 4. Module Mapping

| Current Node File | C# Target | Responsibility |
|---|---|---|
| `server.js` | `Program.cs` and endpoints | API routes, static files, error handling |
| `db.js` | `AppDb.cs` and repositories | SQLite schema, CRUD, query, export data |
| `modbusService.js` | `ModbusService.cs` | RTU connection, read status, select program, start, reset |
| `scannerService.js` | `ScannerService.cs` | Scanner serial port, latest scan, DTR/RTS |
| `testWorkflowService.js` | `TestWorkflowService.cs` | Scan rules, active test flow, save records |
| `public/` | `wwwroot/` | Static frontend files |

## 5. Required API Routes

The C# backend must implement these routes with compatible JSON.

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/health` | Health check |
| `GET` | `/api/config/ateq` | Get instrument serial config |
| `POST` | `/api/config/ateq` | Save instrument config and reconnect |
| `GET` | `/api/config/scanner` | Get scanner serial config |
| `POST` | `/api/config/scanner` | Save scanner config and reconnect |
| `GET` | `/api/settings/products` | List product profiles |
| `POST` | `/api/settings/products` | Save product profiles |
| `GET` | `/api/settings/operators` | List operators |
| `POST` | `/api/settings/operators` | Save operators |
| `GET` | `/api/scanner/latest` | Latest scanner state and scan |
| `GET` | `/api/scanner/debug` | Scanner debug state |
| `POST` | `/api/scanner/debug/line-signals` | Set scanner DTR/RTS |
| `GET` | `/api/status` | Realtime instrument status |
| `GET` | `/api/program-timings` | Read program timing parameters |
| `GET` | `/api/test/active` | Current active test state |
| `POST` | `/api/test/context` | Sync selected product context |
| `POST` | `/api/start` | Start test from software |
| `POST` | `/api/reset` | Reset or stop test |
| `GET` | `/api/tests/latest` | Latest test records |
| `GET` | `/api/tests/query` | Query test records |
| `GET` | `/api/tests/export.csv` | Export records as CSV |

## 6. Important Request Validation

### Communication Config

Fields:

- `comPort`: required string
- `baudrate`: positive integer
- `dataBits`: integer 5 to 8
- `parity`: `none`, `even`, `mark`, `odd`, `space`
- `stopBits`: 1 to 2
- `timeoutMs`: optional, 100 to 5000
- `pollIntervalMs`: optional, 50 to 2000
- `dtr`: optional boolean
- `rts`: optional boolean
- `enabled`: required boolean
- `slaveId`: required for instrument config, 1 to 255

### Product Profiles

Fields:

- `productModel`: required string
- `ateqProgramNo`: integer 1 to 255
- `qrKeyword`: required string
- `isActive`: optional boolean
- `scanConfirmEnabled`: optional boolean
- `scanMatchEnabled`: optional boolean
- `scanAutoStartEnabled`: optional boolean

### Start Request

Fields:

- `productModel`: optional string
- `operatorName`: optional string
- `qrCode`: optional string
- `skipProgramSelect`: optional boolean
- `startMode`: optional, `manual` or `scan`

## 7. Core Models

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
    public bool ScanMatchEnabled { get; set; }
    public bool ScanAutoStartEnabled { get; set; }
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

## 8. SQLite Schema

Use this schema to stay compatible with the current backend.

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

## 9. Modbus Details

Current register map:

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

Keep these conversion rules:

- 16-bit register values need byte swap.
- 32-bit values are built from `lowWord` and `highWord`.
- Pressure and leak values are signed 32-bit values scaled by `/1000`.
- Leak values can be negative.
- Program select writes `programNumber - 1`, then applies byte swap.

Core conversion helpers:

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

Important unit map:

```text
3000  -> mm3/s
51000 -> mL/min
11000 -> Bar
12000 -> kPa
```

Important: unit code `3000` must be `mm3/s`, not `mm3/h`.

## 10. Scanner Behavior

The C# scanner service must keep these behaviors:

- Read scanner input from serial port.
- Buffer incoming data until a complete scan is detected.
- Save accepted scans to `scanner_events`.
- Keep the latest visible scan in memory.
- Clear latest scan after a test record is saved.
- Ignore scans when the instrument is not idle.
- Auto-start only if the selected product allows scan auto-start.
- Support DTR and RTS configuration.

Suggested implementation:

- Use `System.IO.Ports.SerialPort`.
- Use a private lock for the receive buffer.
- Treat CR/LF as scan terminators.
- Keep only recent scanner events in the database.

## 11. Test Workflow Rules

This is the highest-risk migration area.

Keep these internal states:

- `activeRun`
- `pendingContext`
- `selectedContext`
- `lastObservedStepCode`
- `commandInFlight`
- `observeInFlight`
- `lastRejectedObservedRunAt`

Required behavior:

1. `POST /api/test/context` selects the instrument program immediately.
2. Scan auto-start must prefer the selected product context.
3. A QR code must not override the selected product and force program 1.
4. After the first test finishes, the selected product context must remain for the next scan.
5. After a test record is saved, clear the scanner result.
6. If physical-button start violates scan rules, call reset immediately.
7. If polling misses the step 4 edge, recover monitoring when step is between `4` and `100`.
8. Release stale armed context after 8 seconds only when safe.

Physical-button rejection logic:

```text
ObserveTelemetry(status):
  if status enters step 4 OR status is already in active step 4..100:
    ResolveObservedContext(status)
      if product requires scan and no scan:
        reject
      if product requires keyword match and QR mismatch:
        reject
    if rejected:
      ResetDevice()
      do not save a test record
```

Add a 2-second throttle around automatic reset to avoid repeatedly sending reset while polling.

## 12. Background Services

### AteqObserverService

- Runs every `500ms`.
- Calls `ModbusService.ReadRealtimeStatus()`.
- Calls `TestWorkflowService.ObserveTelemetry(status)`.
- Logs Modbus errors without crashing the app.

### ScannerService

- Opens scanner serial port when config is enabled.
- Reconnects after config changes.
- Handles DTR/RTS line signals.
- Calls scan handler after receiving a complete scan.

## 13. Program.cs Skeleton

```csharp
using System.Text.Json;

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

app.MapGet("/api/health", () => new
{
    success = true,
    message = "backend alive"
});

// Add all API endpoints here.

app.Run("http://0.0.0.0:3000");
```

## 14. Recommended Migration Order

Do not migrate everything at once. Use this order:

1. Create ASP.NET Core project.
2. Copy `public/` into `wwwroot/`.
3. Implement `/api/health`.
4. Implement SQLite schema and repositories.
5. Implement product and operator settings APIs.
6. Implement communication config APIs.
7. Implement Modbus connect and `/api/status`.
8. Implement program select, start, and reset.
9. Implement scanner service and `/api/scanner/latest`.
10. Implement test workflow service.
11. Implement test record save, latest records, query, and CSV export.
12. Run field validation on real hardware.

## 15. Acceptance Checklist

The migration is acceptable only after these pass:

- `/`, `/settings.html`, `/comm-config.html`, and `/query.html` open correctly.
- Instrument config can be saved.
- Scanner config can be saved.
- Instrument online status is correct.
- Scanner online status is correct.
- Selecting product 1, 2, or 3 changes the instrument program.
- Scan auto-start uses the selected product, not global QR matching.
- Second scan after first test does not jump back to program 1.
- QR mismatch blocks start.
- Physical-button start without required scan immediately resets/stops the instrument.
- Physical-button start with mismatched scan immediately resets/stops the instrument.
- Test records are saved after completion.
- Scanner result clears after completion.
- Leak trend supports negative values.
- Unit `mm3/s` displays correctly.
- Light/System/Dark appearance works.
- Light mode uses white background and readable dark text.
- A new computer can start with a fresh `data/` folder.

## 16. Do Not Copy These Into the C# Project

Do not copy:

- `node_modules/`
- `runtime18/`
- `data/ateq.db`
- `data/runtime-store.json`
- log files
- temporary test JSON files

If old data must be migrated, write a one-time import tool after the C# schema is stable.

## 17. Main Risks

- Modbus byte order mistakes will break program number, pressure, leak, and unit decoding.
- Serial port callbacks are multi-threaded; protect buffers with locks.
- Workflow start and observe logic must be serialized with `lock` or `SemaphoreSlim`.
- SQLite writes should be serialized.
- Automatic reset after physical-button rejection needs throttling.
- JSON must use camelCase or the existing frontend will break.

## 18. Done Definition

The C# migration is done when:

- The current frontend works without route or field changes.
- All API responses are compatible with the Node version.
- Real hardware tests pass for software start, scan start, and physical-button start.
