# Handover: Leak Test Scan Remote Runtime Issue

## Context

This project runs on the remote Windows PC reachable by Tailscale IP:

```text
100.95.136.69
```

The project folder on the remote PC is:

```text
D:\ATEQ Test\ATEQ-Leak-Test
```

In this coding environment the same folder is usually accessed through:

```text
U:\ATEQ-Leak-Test
```

Important: the service actually runs on the remote PC. Do not assume commands executed from another PC affect the remote runtime unless they are run inside the remote Windows session or through working remote execution.

## Current User-Visible Problem

The user tried to open:

```text
http://127.0.0.1:3000/api/health
```

on the remote PC, but it does not open.

Before it stopped responding, `/api/health` returned the old response:

```json
{"success":true,"message":"ATEQ backend alive"}
```

The expected new response should include:

```json
"build": "monitor-30min-samples-10000"
```

If the `build` field is missing, the old Node process is still running.

## Root Cause Found So Far

`server.err` showed:

```text
Error: listen EADDRINUSE: address already in use :::3000
```

Meaning: a previous/stuck process was already holding port `3000`, so the newly started backend failed to bind to the port.

Later, requests to:

```text
http://100.95.136.69:3000/api/health
```

timed out from this environment, suggesting the process on port `3000` may be stuck: occupying the port but not responding correctly.

## Why This Matters

The original bug was that long program 2 (`120s fill + 120s stabilize + 80s test = 320s`) stopped at about 120 seconds. Records showed examples like:

```text
program: 2
startedAt: 2026-06-09T08:53:35.687Z
finishedAt: 2026-06-09T08:55:35.738Z
duration: about 120s
sampleCount: about 475
```

This matched the old hardcoded backend monitor timeout:

```js
const timeoutMs = 120000;
```

After the monitor ended early, the backend cleared scan context. The instrument was still running, then observer logic treated the continued run as an invalid physical start without scan context and sent reset.

## Code Changes Already Made

These changes are already committed and pushed to:

```text
https://github.com/huaweixiong-debug/Leak-test-scan
```

Recent commits:

```text
d541c6a Add force restart script for stuck port
0967bdd Add runtime health marker and safer restart scripts
1707a5d Keep long test samples for full curves
39833db Set test monitor timeout to 30 minutes
```

### 1. Monitor timeout changed to 30 minutes

File:

```text
testWorkflowService.js
```

Expected constants:

```js
const DEFAULT_MONITOR_TIMEOUT_MS = 30 * 60 * 1000;
const MAX_MONITOR_SAMPLE_COUNT = 10000;
const ACTIVE_SAMPLE_WINDOW_COUNT = 10000;
const SAVED_SAMPLE_WINDOW_COUNT = 10000;
```

The monitoring loop should use:

```js
const timeoutMs = DEFAULT_MONITOR_TIMEOUT_MS;
```

### 2. Long curve samples increased

Old runtime response proved it was still using the old sample window:

```text
activeTest.samples.length = 150
```

New code should use:

```js
state.samples = samples.slice(-ACTIVE_SAMPLE_WINDOW_COUNT);
samples: samples.slice(-SAVED_SAMPLE_WINDOW_COUNT)
```

With the new code active, long program 2 curves should not stop around 50 seconds just because of the frontend sample window.

### 3. Health endpoint now exposes runtime build marker

File:

```text
server.js
```

Expected `/api/health` response:

```json
{
  "success": true,
  "message": "ATEQ backend alive",
  "build": "monitor-30min-samples-10000",
  "monitor": {
    "defaultMonitorTimeoutMs": 1800000,
    "maxMonitorSampleCount": 10000,
    "activeSampleWindowCount": 10000,
    "savedSampleWindowCount": 10000
  }
}
```

If this is not shown, the runtime is not using the updated files.

### 4. Safer restart scripts added/updated

Files:

```text
stop-remote-server.cmd
start-remote-server.cmd
restart-remote-server.cmd
start_vbs.vbs
```

`restart-remote-server.cmd` was added to force kill listeners on port `3000`, start the backend, and print `/api/health`.

## Current Log Files

Expected runtime logs are in:

```text
D:\ATEQ Test\ATEQ-Leak-Test\server.out
D:\ATEQ Test\ATEQ-Leak-Test\server.err
```

From this environment:

```text
U:\ATEQ-Leak-Test\server.out
U:\ATEQ-Leak-Test\server.err
```

Observed `server.err` contained:

```text
Error: listen EADDRINUSE: address already in use :::3000
```

`server.out` was empty at last check.

## Immediate Next Steps for Claude Code

Run these on the remote Windows PC, ideally in an elevated terminal:

```bat
cd /d "D:\ATEQ Test\ATEQ-Leak-Test"
netstat -ano | findstr ":3000"
```

If any line contains `LISTENING`, note the last column PID:

```bat
tasklist /FI "PID eq <PID>"
taskkill /PID <PID> /F
```

Then confirm the port is free:

```bat
netstat -ano | findstr ":3000"
```

Then start:

```bat
restart-remote-server.cmd
```

or:

```bat
start-remote-server.cmd
```

Then test:

```bat
powershell -NoProfile -Command "Invoke-RestMethod http://127.0.0.1:3000/api/health | ConvertTo-Json -Depth 5"
```

Success condition:

```json
"build": "monitor-30min-samples-10000"
```

If `api/health` does not open:

```bat
type server.err
type server.out
netstat -ano | findstr ":3000"
tasklist /FI "IMAGENAME eq node.exe"
```

## Important Diagnostic Commands

From this coding environment:

```powershell
Invoke-RestMethod -Uri 'http://100.95.136.69:3000/api/health' -TimeoutSec 5
Invoke-RestMethod -Uri 'http://100.95.136.69:3000/api/test/active' -TimeoutSec 5
Invoke-RestMethod -Uri 'http://100.95.136.69:3000/api/program-timings?programNumber=2' -TimeoutSec 10
```

Program 2 currently reads correctly as:

```json
{
  "fillTimeSeconds": 120,
  "stabTimeSeconds": 120,
  "testTimeSeconds": 80,
  "totalTimeSeconds": 320
}
```

Program 3 reads as a short program and works:

```json
{
  "fillTimeSeconds": 6.9,
  "stabTimeSeconds": 10.4,
  "testTimeSeconds": 5.4,
  "totalTimeSeconds": 22.7
}
```

## Earlier Related Fixes

Other fixes already made in this project:

- Main page product/operator selection persists across navigation.
- `finalPressure` zero/missing plus `finalLeak=0` displays/saves leak marker `9999`.
- Leak unit code `3000` maps to `mm3/s`.
- Program selection context is preserved so scan auto-start does not jump back to product/program 1.

## Key Hypothesis

The code is fixed on disk and in GitHub, but the remote runtime is still not cleanly restarted.

Until `/api/health` shows:

```json
"build": "monitor-30min-samples-10000"
```

any test behavior still reflects the old process.

