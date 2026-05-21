# @psmon/codescan-cli

> npm install entry point for [CodeScan](https://github.com/psmon/CodeScan).
> CodeScan itself is a .NET 10 single-binary tool — this package is a thin
> wrapper that downloads the right prebuilt binary on `postinstall` and
> forwards your CLI args to it.

## Install

```bash
npm install -g @psmon/codescan-cli
codescan --help
```

> **Heads up — the unscoped `codescan-cli` name is NOT us.**
> An unrelated third party registered the bare `codescan-cli` name on npm
> first. That package is broken (its own ESM/CJS mismatch crashes on launch)
> and has nothing to do with CodeScan. Always install the scoped name
> `@psmon/codescan-cli`. If you previously installed the wrong one, remove
> it with `npm uninstall -g codescan-cli`.

## Supported platforms (v1)

| OS | Architectures |
|----|---------------|
| Linux (glibc) | x64, arm64 |
| macOS         | arm64 (Apple Silicon) |
| Windows       | x64 |

All four shipped binaries (`win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`) are reachable from the npm wrapper — the postinstall picks the right one automatically.

On Windows, `winget install psmon.CodeScan` is still the recommended path (no Node.js required), but if you already have Node installed and prefer a single `npm install -g` line for your toolchain, this package works too.

musl/Alpine Linux and Intel Mac are v2 candidates.

## How it works

`postinstall` (`scripts/install.js`) does the following:

1. Detect OS + CPU arch → asset name like `codescan-linux-x64.tar.gz`.
2. Fetch `https://github.com/psmon/CodeScan/releases/download/v<version>/<asset>`.
3. Fetch `checksums.txt` from the same release and verify SHA256.
4. Extract into `vendor/codescan/` inside this package.
5. `bin/codescan.js` calls that binary with your args.

## Environment overrides

| Variable | Default | Purpose |
|----------|---------|---------|
| `CODESCAN_VERSION` | `package.json` version | Pin a different release |
| `CODESCAN_REPO` | `psmon/CodeScan` | Use a fork |
| `CODESCAN_SKIP_DOWNLOAD` | unset | If `1`, skip the binary download (e.g. for CI where you preinstall manually) |
| `HTTPS_PROXY`, `HTTP_PROXY` | — | Detected and warned about; v1 does not auto-route through proxy |

## User data

CodeScan stores its DB, logs, and config under `~/.codescan/`. That directory
is **never** modified by install or uninstall — it survives upgrades and
package removal. `npm uninstall -g @psmon/codescan-cli` only removes the
vendored binary inside this package.

## Manual install (if `postinstall` is blocked)

If your environment blocks `postinstall` network access:

```bash
# 1. Download the matching asset:
curl -LO https://github.com/psmon/CodeScan/releases/download/v<version>/codescan-linux-x64.tar.gz

# 2. Verify checksum:
curl -LO https://github.com/psmon/CodeScan/releases/download/v<version>/checksums.txt
sha256sum -c checksums.txt --ignore-missing

# 3. Extract somewhere on your PATH:
tar -xzf codescan-linux-x64.tar.gz -C ~/.local/bin --strip-components=1
~/.local/bin/codescan --version
```

## License

MIT — same as CodeScan upstream.
