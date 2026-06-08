const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const requiredMajor = 18;

function exists(relativePath) {
  return fs.existsSync(path.join(root, relativePath));
}

function log(status, message) {
  console.log(`${status} ${message}`);
}

function main() {
  console.log('');
  console.log('Leak Test Scan setup check');
  console.log('==========================');

  let ok = true;
  const major = Number(process.versions.node.split('.')[0]);
  if (Number.isInteger(major) && major >= requiredMajor) {
    log('[OK]', `Node.js ${process.version}`);
  } else {
    ok = false;
    log('[ERR]', `Node.js ${requiredMajor}+ is required. Current: ${process.version}`);
    console.log('      Install Node.js 18 or newer: https://nodejs.org/');
  }

  if (exists('package-lock.json')) {
    log('[OK]', 'package-lock.json found');
  } else {
    log('[WARN]', 'package-lock.json is missing; npm install may resolve newer dependency versions.');
  }

  if (exists('node_modules')) {
    log('[OK]', 'node_modules found');
  } else {
    ok = false;
    log('[TODO]', 'Dependencies are not installed. Run: npm install');
  }

  if (exists('public/index.html') && exists('server.js')) {
    log('[OK]', 'Application files found');
  } else {
    ok = false;
    log('[ERR]', 'Application files are incomplete. Re-pull the repository.');
  }

  if (!exists('data')) {
    log('[INFO]', 'data/ is missing. It will be created automatically on first start.');
  } else {
    log('[OK]', 'data/ found');
  }

  console.log('');
  console.log('First start on a new computer:');
  console.log('  1. npm install');
  console.log('  2. npm start');
  console.log('  3. Open http://127.0.0.1:3000');
  console.log('  4. Open /comm-config.html and set the local COM ports');
  console.log('  5. Open /settings.html and set product models, program numbers, and scan rules');
  console.log('');

  if (!ok) {
    process.exitCode = 1;
  }
}

main();
