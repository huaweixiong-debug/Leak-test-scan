# ATEQ Leak Test — Operations Runbook

## Server

| Property | Value |
|----------|-------|
| Host | `desktop-epprc71` (Tailscale `100.89.253.4`) |
| Project path | `D:\ATEQ` |
| .NET SDK | `C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe` (v8.0.422) |
| Server URL | `http://127.0.0.1:3000` |
| Frontend | `http://127.0.0.1:3000` (operator UI) |
| Config page | `http://127.0.0.1:3000/comm-config.html` |
| Settings page | `http://127.0.0.1:3000/settings.html` |
| Query page | `http://127.0.0.1:3000/query.html` |

## Build & Start

```bat
:: On the remote machine (desktop-epprc71), open a command prompt:
cd /d D:\ATEQ

:: Build (stops existing server, restores packages, compiles)
build.cmd

:: Run interactively (Ctrl+C to stop)
run-server.cmd

:: Or run in background (survives SSH disconnect)
powershell -ExecutionPolicy Bypass -File start-server-bg.ps1

:: Stop background server
stop-server.cmd
```

Verify the server is alive:
```bat
curl http://127.0.0.1:3000/api/health
```
Expected: `{"success":true,"message":"ATEQ backend alive","build":"dotnet-1.0.0",...}`

## Mock Mode (Testing Without Hardware)

### Enable Mock Mode

Edit `D:\ATEQ\src\ATEQ.LeakTest.Web\appsettings.json`:
```json
"FeatureFlags": {
    "EnableMockMode": true
}
```
Then rebuild and restart:
```bat
cd /d D:\ATEQ
build.cmd
run-server.cmd
```

### Configure Mock Devices

Open `http://127.0.0.1:3000/comm-config.html` or use API:
```bat
curl -X POST http://127.0.0.1:3000/api/config/ateq -H "Content-Type: application/json" -d "{\"comPort\":\"MOCK_ATEQ\",\"baudrate\":9600,\"dataBits\":8,\"parity\":\"none\",\"stopBits\":1,\"slaveId\":1,\"timeoutMs\":5000,\"pollIntervalMs\":500,\"dtr\":true,\"rts\":true,\"enabled\":true}"

curl -X POST http://127.0.0.1:3000/api/config/scanner -H "Content-Type: application/json" -d "{\"comPort\":\"MOCK_SCANNER\",\"baudrate\":9600,\"dataBits\":8,\"parity\":\"none\",\"stopBits\":1,\"timeoutMs\":5000,\"pollIntervalMs\":100,\"dtr\":true,\"rts\":true,\"enabled\":true}"
```

### Mock Test Flow

1. Open `http://127.0.0.1:3000/settings.html` → create a product (e.g., model "TP", program 1, QR keyword "T")
2. Open `http://127.0.0.1:3000/index.html` → select product, click Start
3. Wait ~5 seconds for the simulated test to complete
4. Check result on the main page or `http://127.0.0.1:3000/query.html`

### Control Mock Result

```bat
:: Set next test to pass (OK)
curl -X POST http://127.0.0.1:3000/api/debug/mock/ateq/next-result -H "Content-Type: application/json" -d "{\"resultCode\":\"OK\"}"

:: Set next test to fail (NG) with alarm
curl -X POST http://127.0.0.1:3000/api/debug/mock/ateq/next-result -H "Content-Type: application/json" -d "{\"resultCode\":\"NG\",\"errorCode\":\"ATEQ_ALARM\"}"
```

### Inject Mock Scan

```bat
curl -X POST http://127.0.0.1:3000/api/scanner/debug/mock-scan -H "Content-Type: application/json" -d "{\"rawText\":\"YOUR-QR-CODE\"}"
```

### Disable Mock Mode

Set `"EnableMockMode": false` in `appsettings.json`, rebuild, restart. All mock endpoints return 404 and MOCK_* config saves are rejected with 400.

## Real Hardware Setup

### Configure ATEQ

Open `http://127.0.0.1:3000/comm-config.html` or use API:
```bat
curl -X POST http://127.0.0.1:3000/api/config/ateq -H "Content-Type: application/json" -d "{\"comPort\":\"COM7\",\"baudrate\":9600,\"dataBits\":8,\"parity\":\"even\",\"stopBits\":1,\"slaveId\":255,\"timeoutMs\":5000,\"pollIntervalMs\":500,\"dtr\":true,\"rts\":true,\"enabled\":true}"
```

**Important**: The COM port number depends on the physical machine. Check Windows Device Manager → Ports (COM & LPT) for the correct port.

The documented ATEQ parameters from the original deployment:
| Parameter | Value |
|-----------|-------|
| COM port | COM7 (verify on target machine) |
| Baudrate | 9600 |
| Data bits | 8 |
| Parity | even |
| Stop bits | 1 |
| Slave ID | 255 |

### Configure Scanner — Three Modes

The system supports three scanner modes. Choose one:

**Mode 1: KEYBOARD_WEDGE (Keyence HR-X100 USB)** — recommended for HR-X100

```bat
curl -X POST http://127.0.0.1:3000/api/config/scanner -H "Content-Type: application/json" -d "{\"comPort\":\"KEYBOARD_WEDGE\",\"baudrate\":115200,\"dataBits\":8,\"parity\":\"none\",\"stopBits\":1,\"timeoutMs\":5000,\"pollIntervalMs\":100,\"dtr\":true,\"rts\":false,\"enabled\":true}"
```

- No serial cable needed. Scanner types barcodes as keyboard input.
- Browser page must have focus. Typing in inputs/selects is ignored.
- Verify: `curl http://127.0.0.1:3000/api/scanner/latest` → `"connected":true`
- Scans are persisted immediately regardless of ATEQ state.
- Scan-triggered auto-start only fires if ATEQ is idle (step 65535).

**Mode 2: COMx (real RS232 serial scanner)**

```bat
curl -X POST http://127.0.0.1:3000/api/config/scanner -H "Content-Type: application/json" -d "{\"comPort\":\"COM1\",\"baudrate\":115200,\"dataBits\":8,\"parity\":\"none\",\"stopBits\":1,\"timeoutMs\":5000,\"pollIntervalMs\":100,\"dtr\":true,\"rts\":true,\"enabled\":true}"
```

| Parameter | Value |
|-----------|-------|
| COM port | COM1 (verify on target machine) |
| Baudrate | 115200 |
| Data bits | 8 |
| Parity | none |
| Stop bits | 1 |

**Mode 3: MOCK_SCANNER (testing without hardware)** — requires `EnableMockMode: true`

```bat
curl -X POST http://127.0.0.1:3000/api/config/scanner -H "Content-Type: application/json" -d "{\"comPort\":\"MOCK_SCANNER\",\"baudrate\":115200,\"dataBits\":8,\"parity\":\"none\",\"stopBits\":1,\"timeoutMs\":5000,\"pollIntervalMs\":100,\"dtr\":true,\"rts\":true,\"enabled\":true}"
```

### Scan Persistence vs Auto-Start

- **Scan persistence**: Every scan received through `/api/scanner/input` is saved to `scanner_events` unconditionally. View with `GET /api/scanner/history?take=20`.
- **Auto-start**: Only triggers if ATEQ is connected AND idle (step 65535) AND the scanned QR matches a product with `scanAutoStartEnabled: true`. If ATEQ is busy or offline, the scan is still saved but the test does not auto-start.

## Field Verification Checklist

After connecting real hardware, verify:

### 1. ATEQ Device Connection
```bat
curl http://127.0.0.1:3000/api/status
```
Expected when connected: `"connected":true`, real-time telemetry with pressure/leak values.
If not connected: `"errorCode"` will show the specific failure (serial port error, Modbus timeout, etc.).

### 2. Scanner Connection
```bat
curl http://127.0.0.1:3000/api/scanner/latest
curl http://127.0.0.1:3000/api/scanner/debug
```
**KEYBOARD_WEDGE mode**: `"connected":true` immediately after config save. Scan by focusing the page and scanning a barcode — the keyboard wedge listener POSTs to `/api/scanner/input`. Verify with `curl http://127.0.0.1:3000/api/scanner/history?take=5`.

**COMx serial mode**: `"connected":true` when serial port opens. `"bytesReceived"` > 0 after scanning. Check `"modemSignals"` if scanner appears offline.

### 3. Product Profile Setup
Open `http://127.0.0.1:3000/settings.html` and verify:
- Product models configured with correct ATEQ program numbers
- QR keywords match the barcodes you will scan
- Scan auto-start enabled for products that should start on scan

### 4. Full Test Flow
1. Select product and operator on `http://127.0.0.1:3000/index.html`
2. Click Start (or scan a barcode if auto-start is enabled)
3. Verify the UI shows test progress (step changes, pressure/leak values)
4. Verify the test completes and a result (OK/NG) is displayed
5. Check `http://127.0.0.1:3000/query.html` — the record should appear

### 5. Program Timings
```bat
curl "http://127.0.0.1:3000/api/program-timings?programNumber=1"
```
Verify the fill/stab/test timings match the ATEQ device's configured program.

## Quick Reference — API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/health` | Server alive check |
| GET | `/api/status` | ATEQ real-time telemetry |
| GET | `/api/test/active` | Current test workflow state |
| GET | `/api/scanner/latest` | Latest barcode scan |
| GET | `/api/scanner/debug` | Scanner raw data diagnostics |
| GET | `/api/config/ateq` | Read ATEQ serial config |
| GET | `/api/config/scanner` | Read scanner serial config |
| POST | `/api/config/ateq` | Save ATEQ serial config |
| POST | `/api/config/scanner` | Save scanner serial config |
| GET | `/api/settings/products` | List product profiles |
| POST | `/api/settings/products` | Save product profiles |
| GET | `/api/settings/operators` | List operators |
| POST | `/api/settings/operators` | Save operators |
| POST | `/api/start` | Start a test |
| POST | `/api/reset` | Reset/cancel current test |
| POST | `/api/test/context` | Sync product/operator context |
| GET | `/api/tests/latest` | Recent test records |
| GET | `/api/tests/query?...` | Search/filter test records |
| GET | `/api/tests/export.csv?...` | Export records as CSV |
| GET | `/api/program-timings?programNumber=N` | Read ATEQ program timings |

## Troubleshooting

| Symptom | Check |
|---------|-------|
| `/api/health` returns 404, `/scanner-test.html` 404, static HTML pages look wrong | **You are hitting the old Node.js server, not the C# server.** The C# server returns `{"success":true,"build":"dotnet-1.0.0"}` from `/api/health`. Check port 3000: `netstat -ano \| findstr ":3000"`. If `node.exe` owns it, run `taskkill /F /IM node.exe` then restart with `run-server.cmd`. The legacy `run-node-server.cmd` and `start-local.cmd` are now deprecated and will refuse to start. |
| Server won't start | `netstat -ano \| findstr ":3000"` — if port is occupied by non-dotnet process (e.g., `node.exe`), the C# startup scripts will now warn explicitly. Kill the wrong process first. |
| ATEQ shows offline | Verify COM port in Device Manager matches config; check cable; check ATEQ power |
| Scanner shows offline | Check `GET /api/scanner/debug` for modem signals; verify baud rate matches scanner manual |
| Tests stuck at "armed" | ATEQ not responding — check `/api/status` for error detail |
| Records not saving | Check `D:\ATEQ\server.log` for SQLite errors |
| Build fails | Run `build.cmd`; check .NET SDK version with `dotnet --version` |
| Mock endpoints return 404 | `EnableMockMode` is `false` in `appsettings.json` — this is the production-safe default |

## Rollback to Real Hardware

If mock mode was enabled for testing, revert to real hardware:

1. Set `"EnableMockMode": false` in `appsettings.json`
2. Rebuild: `build.cmd`
3. Restart: `run-server.cmd`
4. Configure real COM ports at `http://127.0.0.1:3000/comm-config.html`
5. Verify with `curl http://127.0.0.1:3000/api/status`
