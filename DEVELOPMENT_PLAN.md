# ATEQ Leak Test Development Plan

## 1. Project Goal

Build a Windows-based upper computer application for the ATEQ leak tester and RS232 scanner.

Primary goals:

- Read scanner data from a dedicated RS232 COM port.
- Control and read ATEQ via RS232 Modbus.
- Support product profile binding, operator binding, auto-start by scan, manual start by product selection, history query, and export.
- Run reliably on the remote industrial PC at `100.95.136.69`.

Current implementation is a Node.js 18 + Express backend with a lightweight browser debug page.

## 2. Runtime Environment

- Remote host: `100.95.136.69`
- Remote project path: `D:\ATEQ Test\ATEQ-Leak-Test`
- Mapped workspace path: `U:\ATEQ-Leak-Test`
- Runtime: portable Node 18 in `runtime18\node-v18.20.8-win-x64`
- Start script: `start-remote-server.cmd`
- Stop script: `stop-remote-server.cmd`
- Debug page: `http://127.0.0.1:3000/`

## 3. Confirmed Device Parameters

### 3.1 ATEQ

- COM port: `COM7`
- Baudrate: `9600`
- Data bits: `8`
- Parity: `even`
- Stop bits: `1`
- Slave ID: `255`

### 3.2 Scanner

- COM port: `COM1`
- Baudrate: `115200`
- Data bits: `8`
- Parity: `none`
- Stop bits: `1`
- Flow control: `none`

## 4. Current Codebase Status

### 4.1 Already implemented

- `server.js`
  - Config APIs
  - Product/operator config APIs
  - Start/reset/status APIs
  - History query/export APIs
  - Scanner debug APIs
  - Static debug page hosting
- `modbusService.js`
  - ATEQ Modbus RTU connection
  - Read realtime telemetry
  - Program select
  - Start/reset
- `scannerService.js`
  - COM scanner connection
  - Raw chunk capture
  - Latest scan publish
  - DTR/RTS line-signal switching
  - Raw serial debug state
- `testWorkflowService.js`
  - Product resolve
  - Manual/scan start flow
  - Step 6 final pressure capture
  - Step 65535 final leak capture
  - Record save
- `db.js`
  - JSON persistence for configs, profiles, scans, test records
  - History filter and pagination
- `public/index.html`
  - Scanner debug page
  - ATEQ realtime status
  - DTR/RTS manual test buttons

### 4.2 Known technical debt

- Persistence is still JSON-based, not SQLite.
- Frontend is only a debug page, not the final production UI.
- Some Chinese strings in legacy areas were previously encoding-damaged and should be cleaned during later refactor.
- Remote restart through SSH sometimes times out even when the service is already running correctly.

## 5. Current Diagnostic Conclusion

At the time of this handoff:

- ATEQ communication is readable from backend.
- Scanner debug page and raw serial debug API are working.
- Scanner line-signal switching is exposed in UI and API.
- If scanner still fails under this backend while it works in a serial assistant, the remaining gap is likely in low-level serial open behavior rather than application-layer parsing.

Reasonix should treat scanner bring-up as a first-class stabilization task before expanding UI.

## 6. Recommended Delivery Order

### Phase 1: Stabilize scanner input

Goal:

- Make scanner input fully reliable in backend without depending on serial assistant.

Tasks:

1. Compare scanner behavior across:
   - current Node backend
   - serial assistant
2. Verify whether any of these affect receive behavior:
   - DTR
   - RTS
   - open timing
   - read timeout pattern
   - line ending behavior
   - exclusive access differences
3. If needed, add:
   - reconnect button
   - line reset command
   - configurable idle flush time
   - configurable text encoding
4. Acceptance:
   - scanner debug page shows `bytesReceived > 0`
   - latest scan updates correctly
   - repeated scans do not require reopening the port

### Phase 2: Replace JSON persistence with SQLite

Goal:

- Move all persistent data to SQLite while preserving existing API contracts.

Tasks:

1. Add SQLite wrapper module.
2. Migrate:
   - comm configs
   - product profiles
   - operators
   - scan events
   - test records
   - optional test samples
3. Keep route contracts stable.
4. Add migration/init logic on startup.
5. Acceptance:
   - restart-safe
   - query/export still work
   - no data loss on restart

Suggested tables:

- `operators`
- `product_profiles`
- `comm_configs`
- `scan_events`
- `test_records`
- `test_samples`

### Phase 3: Build the actual production pages

Goal:

- Replace the current debug page with operator-facing workflow pages.

Required pages:

1. Settings page
   - product model
   - ATEQ program number
   - QR keyword
   - operator maintenance
2. Main test page
   - latest scan display
   - realtime pressure and leak
   - final pressure
   - final leak
   - OK/NG result
   - pressure curve
   - leak curve
   - manual start
   - scan auto-start
3. Query page
   - time range filter
   - product filter
   - result filter
   - QR exact/fuzzy search
   - export
4. Communication page
   - ATEQ COM config
   - scanner COM config
   - connection state
   - scanner raw debug

### Phase 4: Harden test workflow

Goal:

- Make the test flow production-safe.

Tasks:

1. Add explicit workflow states:
   - idle
   - waiting_scan
   - matched_product
   - selecting_program
   - resetting
   - starting
   - testing
   - finished
   - failed
2. Add duplicate-start guard.
3. Add port reconnect strategy.
4. Add ATEQ timeout and result fallback strategy.
5. Add clearer alarm/error mapping.
6. Persist test samples only when needed or behind a config flag.

### Phase 5: Final packaging and deployment

Goal:

- Deliver a reliable desktop-run solution for the industrial PC.

Options:

- Keep Node + browser UI if acceptable for operations.
- Or package with Electron if a single desktop executable is required.

Tasks:

1. Finalize startup scripts.
2. Add log rotation or bounded log size.
3. Add backup/export for SQLite DB.
4. Document upgrade procedure.

## 7. API Priorities

### Existing core APIs

- `GET /api/health`
- `GET /api/status`
- `POST /api/start`
- `POST /api/reset`
- `GET /api/config/ateq`
- `POST /api/config/ateq`
- `GET /api/config/scanner`
- `POST /api/config/scanner`
- `GET /api/settings/products`
- `POST /api/settings/products`
- `GET /api/settings/operators`
- `POST /api/settings/operators`
- `GET /api/tests/latest`
- `GET /api/tests/query`
- `GET /api/tests/export.csv`
- `GET /api/scanner/latest`
- `GET /api/scanner/debug`
- `POST /api/scanner/debug/line-signals`
- `GET /api/test/active`

### Recommended additions

- `POST /api/scanner/reconnect`
- `POST /api/scanner/clear-debug`
- `GET /api/test/live-samples`
- `POST /api/test/abort`

## 8. Data Rules

### Test result capture rules

- Final pressure: use the last sample in the last 1 second of `stepCode = 6`
- Final leak: use value at `stepCode = 65535`
- Daily sequence:
  - scoped by date + product model
  - 4 digits
  - starts from `0001` every day

### Scan-to-product match rule

- Match by product profile `qrKeyword`
- Auto-start only when a profile is matched and scanner input is validated

## 9. Recommended Module Boundaries

- `server.js`
  - route binding
  - request validation
  - response shaping
- `modbusService.js`
  - ATEQ transport only
- `scannerService.js`
  - scanner transport only
- `testWorkflowService.js`
  - workflow state machine only
- `db.js`
  - persistence only
- future `ui/` or `public/app.js`
  - page logic only

Do not collapse transport logic, workflow logic, and persistence into one file.

## 10. Verification Plan

### Scanner

- Scan 10 times continuously
- Confirm:
  - raw bytes received
  - latest scan updated
  - no duplicate reads
  - no stale buffer carryover

### ATEQ

- Read status repeatedly for at least 3 minutes
- Verify:
  - no disconnect loop
  - correct step code mapping
  - stable pressure/leak values

### Workflow

- Manual start:
  - choose product
  - start
  - confirm result saved
- Scan start:
  - scan valid QR
  - auto-match product
  - auto-start
  - confirm result saved

### Query/export

- Query by:
  - time range
  - product
  - result
  - QR exact
  - QR fuzzy
- Export CSV and verify fields

## 11. Immediate Next Task for Reasonix

Reasonix should continue from this exact order:

1. Finish scanner stabilization until backend can receive raw bytes reliably without serial assistant.
2. Once scanner input is stable, clean the debug page into a more structured operator page.
3. Replace JSON storage with SQLite.
4. Complete production UI pages.
5. Harden workflow and deploy.

## 12. Handoff Note

The project is already runnable on the remote machine.

Use these as the working truth:

- remote execution path: `D:\ATEQ Test\ATEQ-Leak-Test`
- service URL: `http://127.0.0.1:3000/`
- ATEQ is already readable
- scanner raw debug API already exists
- scanner currently needs further low-level stabilization

Reasonix should continue by extending the existing codebase, not rewriting it from scratch.
