# ATEQ Leak Test TODO

## P0 - Scanner Bring-up

- [ ] Confirm scanner can be read by backend without serial assistant.
- [ ] Test all DTR/RTS combinations from the debug page.
- [ ] Record which combination, if any, makes `bytesReceived > 0`.
- [ ] If all four fail, compare serial assistant open behavior against backend open behavior.
- [ ] Add a scanner reconnect action if needed.
- [ ] Add a clear-debug action if needed.

Acceptance:

- [ ] `GET /api/scanner/debug` shows `bytesReceived > 0` after scan.
- [ ] `GET /api/scanner/latest` returns the scanned text.
- [ ] Repeated scans work without reopening the COM port manually.

## P0 - ATEQ Communication Stability

- [ ] Verify ATEQ stays connected on `COM7 / 9600 / 8E1 / slaveId 255`.
- [ ] Verify status polling remains stable for at least 3 minutes.
- [ ] Verify step code, pressure, and leak values are decoded correctly.
- [ ] Verify reset and start commands behave correctly on the real device.

Acceptance:

- [ ] `GET /api/status` remains readable under repeated polling.
- [ ] `POST /api/reset` and `POST /api/start` work on real hardware.

## P1 - Workflow Validation

- [ ] Verify manual start by product model.
- [ ] Verify scan-to-product matching by `qrKeyword`.
- [ ] Verify auto-start after valid scan.
- [ ] Verify final pressure capture rule:
  - last sample in final 1 second of `stepCode = 6`
- [ ] Verify final leak capture rule:
  - value at `stepCode = 65535`
- [ ] Verify OK/NG result storage.

Acceptance:

- [ ] One successful manual test record is saved.
- [ ] One successful scan-triggered test record is saved.
- [ ] Saved result fields match real device output.

## P1 - Storage Migration

- [ ] Replace JSON persistence with SQLite.
- [ ] Keep existing API contracts stable during migration.
- [ ] Add DB init and migration logic.
- [ ] Move these data sets into SQLite:
  - comm configs
  - product profiles
  - operators
  - scan events
  - test records
  - optional test samples

Acceptance:

- [ ] Restart does not lose config or history.
- [ ] Query/export still work after migration.

## P1 - Query and Export Hardening

- [ ] Verify time-range filtering.
- [ ] Verify product filter.
- [ ] Verify result filter.
- [ ] Verify QR exact search.
- [ ] Verify QR fuzzy search.
- [ ] Verify CSV export content and encoding.

Acceptance:

- [ ] Query results match stored records.
- [ ] Exported CSV opens correctly and fields are complete.

## P2 - Production UI

- [ ] Build Settings page.
- [ ] Build Main Test page.
- [ ] Build Query page.
- [ ] Build Communication page.
- [ ] Preserve current debug page until production pages are stable.

Settings page:

- [ ] Product model maintenance.
- [ ] ATEQ program number binding.
- [ ] QR keyword binding.
- [ ] Operator maintenance.

Main Test page:

- [ ] Latest scan display.
- [ ] Realtime pressure display.
- [ ] Realtime leak display.
- [ ] Final pressure display.
- [ ] Final leak display.
- [ ] OK/NG result display.
- [ ] Pressure curve.
- [ ] Leak curve.
- [ ] Manual start.
- [ ] Scan auto-start.

Query page:

- [ ] Time-range search.
- [ ] Product search.
- [ ] Result search.
- [ ] QR search.
- [ ] Export action.

Communication page:

- [ ] ATEQ COM settings.
- [ ] Scanner COM settings.
- [ ] Connection status.
- [ ] Scanner raw debug panel.

## P2 - Workflow Hardening

- [ ] Add explicit workflow states.
- [ ] Add duplicate-start guard.
- [ ] Add better reconnect strategy.
- [ ] Add clearer timeout and alarm handling.
- [ ] Add optional test sample persistence switch.
- [ ] Add abort/stop behavior if required by operations.

Acceptance:

- [ ] Workflow transitions are visible and deterministic.
- [ ] Error messages are clear enough for operators and maintenance staff.

## P3 - Packaging and Deployment

- [ ] Decide final runtime shape:
  - browser UI + Node service
  - or Electron desktop package
- [ ] Finalize startup and stop scripts.
- [ ] Add bounded log handling.
- [ ] Add backup/export procedure for persistent data.
- [ ] Write deployment and upgrade notes.

Acceptance:

- [ ] System can be restarted by local staff using a documented process.
- [ ] Upgrade does not lose history or communication settings.

## Current Known Facts

- [x] Remote project path is `D:\ATEQ Test\ATEQ-Leak-Test`.
- [x] Mapped workspace path is `U:\ATEQ-Leak-Test`.
- [x] ATEQ parameters are confirmed.
- [x] Scanner serial parameters are confirmed.
- [x] Debug page is online at `http://127.0.0.1:3000/`.
- [x] Raw scanner debug API exists.
- [x] ATEQ status API exists.
- [x] History query/export APIs exist.
- [x] Development plan exists in `DEVELOPMENT_PLAN.md`.

## Recommended Next Action

- [ ] Finish scanner backend bring-up first.

Reason:

- Scanner is still the main blocker for the real production flow.
- UI expansion before scanner stability will create rework.
