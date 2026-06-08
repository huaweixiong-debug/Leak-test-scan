const path = require('path');

const REQUIRED_MAJOR = 18;

function fail(message, details = []) {
  console.error('');
  console.error('[start] ' + message);
  for (const detail of details) {
    console.error('        ' + detail);
  }
  console.error('');
  process.exit(1);
}

const major = Number(process.versions.node.split('.')[0]);
if (!Number.isInteger(major) || major < REQUIRED_MAJOR) {
  fail(`Node.js ${REQUIRED_MAJOR}+ is required. Current version: ${process.version}`, [
    'Install Node.js 18 or newer, then run: npm install',
    'Download: https://nodejs.org/'
  ]);
}

try {
  require.resolve('express');
  require.resolve('modbus-serial');
  require.resolve('serialport');
  require.resolve('sql.js');
} catch (error) {
  fail('Dependencies are not installed.', [
    `Project folder: ${path.resolve(__dirname, '..')}`,
    'Run: npm install',
    'Then run: npm start',
    'Windows users can also double-click: start-local.cmd'
  ]);
}

require('../server');
