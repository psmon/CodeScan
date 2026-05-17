#!/usr/bin/env node
//
// preuninstall: remove the vendored binary so npm can cleanly drop the package.
// Does NOT touch ~/.codescan/ (user data is preserved).

'use strict';

const fs = require('fs');
const path = require('path');

const vendor = path.resolve(__dirname, '..', 'vendor');
try {
    if (fs.existsSync(vendor)) {
        fs.rmSync(vendor, { recursive: true, force: true });
        process.stdout.write('codescan-cli: removed vendor/\n');
    }
} catch (e) {
    process.stderr.write('codescan-cli: vendor/ cleanup failed (' + e.message + ') — safe to ignore.\n');
}
