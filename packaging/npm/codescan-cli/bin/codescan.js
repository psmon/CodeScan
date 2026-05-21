#!/usr/bin/env node
//
// Thin launcher — forwards all argv to the CodeScan native binary installed
// by scripts/install.js into ../vendor/codescan/.

'use strict';

const path = require('path');
const { spawnSync } = require('child_process');
const fs = require('fs');

const vendorBin = path.resolve(__dirname, '..', 'vendor', 'codescan',
    process.platform === 'win32' ? 'codescan.exe' : 'codescan');

if (!fs.existsSync(vendorBin)) {
    console.error('@psmon/codescan-cli: binary not found at ' + vendorBin);
    console.error('@psmon/codescan-cli: did `npm install -g @psmon/codescan-cli` complete its postinstall step?');
    console.error('@psmon/codescan-cli: try reinstalling, or follow the manual install:');
    console.error('@psmon/codescan-cli:   https://github.com/psmon/CodeScan/releases/latest');
    process.exit(1);
}

const result = spawnSync(vendorBin, process.argv.slice(2), {
    stdio: 'inherit',
    windowsHide: false
});

if (result.error) {
    console.error('@psmon/codescan-cli: failed to spawn binary:', result.error.message);
    process.exit(1);
}
process.exit(result.status ?? 0);
