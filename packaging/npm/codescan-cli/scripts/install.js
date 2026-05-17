#!/usr/bin/env node
//
// postinstall: download the CodeScan binary for the current OS/arch from
// GitHub Releases, verify SHA256, extract into ../vendor/codescan/.
//
// User data at ~/.codescan/{db,logs,config} is never touched.

'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const https = require('https');
const { execSync } = require('child_process');

const REPO = process.env.CODESCAN_REPO || 'psmon/CodeScan';

function log(msg)  { process.stdout.write('codescan-cli: ' + msg + '\n'); }
function warn(msg) { process.stderr.write('codescan-cli: ' + msg + '\n'); }
function die(msg)  { warn(msg); process.exit(1); }

// 1. Skip if user opted out (CI builds, mirror caches, etc.)
if (process.env.CODESCAN_SKIP_DOWNLOAD === '1') {
    log('CODESCAN_SKIP_DOWNLOAD=1 — skipping binary download.');
    process.exit(0);
}

// 2. Map platform → RID
const rid = (() => {
    const arch = ({ x64: 'x64', arm64: 'arm64' })[process.arch];
    if (!arch) die('Unsupported CPU arch: ' + process.arch);
    if (process.platform === 'linux')  return 'linux-' + arch;
    if (process.platform === 'darwin') return 'osx-'   + arch;
    if (process.platform === 'win32')  return 'win-'   + arch;
    die('Unsupported platform: ' + process.platform);
})();
const ext   = process.platform === 'win32' ? 'zip' : 'tar.gz';
const asset = `codescan-${rid}.${ext}`;

// 3. Read version from the npm package itself.
const pkg = require(path.resolve(__dirname, '..', 'package.json'));
const version = process.env.CODESCAN_VERSION || pkg.version;
if (!version || version === '0.0.0') {
    die('package.json version is unset (0.0.0). Set $CODESCAN_VERSION or publish with a real version.');
}
const tag = version.startsWith('v') ? version : 'v' + version;

const base = `https://github.com/${REPO}/releases/download/${tag}`;
const assetUrl = `${base}/${asset}`;
const sumsUrl  = `${base}/checksums.txt`;

// 4. Honor proxies.
function buildAgentOpts() {
    const proxy = process.env.HTTPS_PROXY || process.env.https_proxy ||
                  process.env.HTTP_PROXY  || process.env.http_proxy;
    if (!proxy) return undefined;
    warn('Proxy detected (' + proxy + ') — postinstall does not yet auto-route through it.');
    warn('If download fails, install manually from ' + base);
    return undefined;
}

function get(url) {
    return new Promise((resolve, reject) => {
        const opts = { headers: { 'User-Agent': 'codescan-cli-installer' } };
        const agent = buildAgentOpts();
        if (agent) opts.agent = agent;
        const doReq = (u) => https.get(u, opts, (res) => {
            if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
                res.resume();
                return doReq(res.headers.location);
            }
            if (res.statusCode !== 200) {
                res.resume();
                return reject(new Error(`HTTP ${res.statusCode} for ${u}`));
            }
            resolve(res);
        }).on('error', reject);
        doReq(url);
    });
}

async function download(url, destPath) {
    const res = await get(url);
    await new Promise((resolve, reject) => {
        const out = fs.createWriteStream(destPath);
        res.pipe(out);
        out.on('finish', resolve);
        out.on('error', reject);
    });
}

async function fetchText(url) {
    const res = await get(url);
    return new Promise((resolve, reject) => {
        let buf = '';
        res.on('data', (chunk) => { buf += chunk.toString('utf8'); });
        res.on('end',  () => resolve(buf));
        res.on('error', reject);
    });
}

function sha256File(file) {
    const h = crypto.createHash('sha256');
    h.update(fs.readFileSync(file));
    return h.digest('hex');
}

async function main() {
    const root  = path.resolve(__dirname, '..');
    const tmp   = fs.mkdtempSync(path.join(os.tmpdir(), 'codescan-install-'));
    const dl    = path.join(tmp, asset);

    log(`Resolved release ${tag} for ${rid}`);
    log(`Downloading ${assetUrl}`);
    try {
        await download(assetUrl, dl);
    } catch (e) {
        die(`Download failed: ${e.message}\n  Manual install: ${base}/${asset}`);
    }

    log('Verifying checksum');
    let expected = null;
    try {
        const sums = await fetchText(sumsUrl);
        for (const line of sums.split(/\r?\n/)) {
            if (line.includes(asset)) {
                expected = line.trim().split(/\s+/)[0];
                break;
            }
        }
    } catch (e) {
        warn(`Could not fetch checksums.txt (${e.message}) — proceeding without verification (NOT RECOMMENDED).`);
    }
    if (expected) {
        const actual = sha256File(dl);
        if (actual.toLowerCase() !== expected.toLowerCase()) {
            die(`Checksum mismatch:\n  expected: ${expected}\n  actual:   ${actual}`);
        }
        log(`SHA256 verified: ${actual}`);
    }

    // 5. Extract.
    const vendor = path.join(root, 'vendor');
    if (fs.existsSync(vendor)) fs.rmSync(vendor, { recursive: true, force: true });
    fs.mkdirSync(vendor, { recursive: true });

    log(`Extracting into ${vendor}`);
    if (ext === 'zip') {
        // Try PowerShell first, then unzip.
        try {
            execSync(`powershell -NoProfile -Command "Expand-Archive -Path '${dl}' -DestinationPath '${vendor}' -Force"`, { stdio: 'inherit' });
        } catch {
            try { execSync(`unzip -o "${dl}" -d "${vendor}"`, { stdio: 'inherit' }); }
            catch (e) { die('No unzip tool available. Install one or extract manually: ' + e.message); }
        }
    } else {
        execSync(`tar -xzf "${dl}" -C "${vendor}"`, { stdio: 'inherit' });
    }

    // Make sure the binary is executable on Unix.
    const exe = process.platform === 'win32' ? 'codescan.exe' : 'codescan';
    const finalBin = path.join(vendor, 'codescan', exe);
    if (!fs.existsSync(finalBin)) {
        die(`Expected ${finalBin} after extraction but it was not found.`);
    }
    if (process.platform !== 'win32') {
        fs.chmodSync(finalBin, 0o755);
    }
    log(`Installed binary at ${finalBin}`);

    // Cleanup
    try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { /* ignore */ }
    log('Done.');
}

main().catch((e) => die(e.stack || e.message));
