# Leak Test Scan

Node.js backend and static web console for leak-test scanning, product program selection, scan matching, test recording, and serial communication configuration.

## New Computer Quick Start

1. Install Node.js 18 or newer: https://nodejs.org/
2. Clone or pull this repository.
3. In the project folder, double-click `start-local.cmd`.
4. Open `http://127.0.0.1:3000`.
5. Open `http://127.0.0.1:3000/comm-config.html` and set the local COM ports.
6. Open `http://127.0.0.1:3000/settings.html` and set product models, program numbers, QR keywords, scan matching, and scan auto-start.

`start-local.cmd` checks Node.js, installs npm dependencies when `node_modules/` is missing, runs a setup check, and starts the service.

## Command Line Start

```bat
npm install
npm run doctor
npm start
```

Then open:

```text
http://127.0.0.1:3000
```

## What Is Not Stored In Git

The following are intentionally not pushed to GitHub:

- `node_modules/`: installed by `npm install`.
- `data/`: local database, product settings, communication settings, and test records.
- `runtime18/`: local bundled runtime from the original machine.
- Logs and temporary request files.

On a fresh computer, the app creates `data/` automatically. You still need to configure local COM ports and product rules once, because COM numbers and hardware wiring are different on different computers.

If you want another computer to keep the same product settings and historical records, copy the `data/` folder manually from the original computer after stopping the service.

## Troubleshooting

- If `Node.js was not found`, install Node.js 18+ and run `start-local.cmd` again.
- If `npm install failed`, check network access or npm registry settings.
- If the page opens but the instrument or scanner is offline, update `/comm-config.html` with the COM ports shown in Windows Device Manager.
- If scanning works but the program number is wrong, check `/settings.html` product model, program number, and QR keyword rules.
