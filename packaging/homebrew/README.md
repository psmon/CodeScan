# Homebrew packaging

Tap repository: `psmon/homebrew-codescan` (separate repo, manually created).

## Files

| Path | Purpose |
|------|---------|
| `codescan.rb` | Formula template with `VERSION_PLACEHOLDER` + two SHA256 placeholders |
| `update-formula.sh` | Rewrites placeholders for a given release and emits the final formula to stdout |

## v1 release flow (manual)

1. Publish a GitHub Release (osx-arm64 + osx-x64 tarballs + checksums.txt).
2. From the CodeScan repo:
   ```bash
   ./packaging/homebrew/update-formula.sh 0.3.94 ./dist/checksums.txt > /tmp/codescan.rb
   ```
3. Push `/tmp/codescan.rb` to the tap repo at `Formula/codescan.rb`:
   ```bash
   git -C path/to/homebrew-codescan checkout -b release/0.3.94
   cp /tmp/codescan.rb path/to/homebrew-codescan/Formula/codescan.rb
   git -C path/to/homebrew-codescan add Formula/codescan.rb
   git -C path/to/homebrew-codescan commit -m "codescan 0.3.94"
   git -C path/to/homebrew-codescan push origin release/0.3.94
   ```
4. Verify locally:
   ```bash
   brew install --build-from-source ./Formula/codescan.rb
   brew test codescan
   brew audit --strict codescan
   ```
5. Merge into the tap's main branch.

## User installation

```bash
brew tap psmon/codescan
brew install codescan
codescan --version
```

Or one-shot:

```bash
brew install psmon/codescan/codescan
```

## v1.x automation

`.github/workflows/release.yml` contains an `if: false`-gated `publish-homebrew`
job. To enable: create `secrets.HOMEBREW_TAP_TOKEN` (a PAT with push rights to
the tap repo), replace the `echo "TODO: ..."` step with the actual clone+push
flow, and flip `if:` to `true`.

## Notarization (v2 candidate)

v1 binaries are not notarized. macOS users will see a Gatekeeper warning on
first run. The formula's `caveats` block documents the `xattr -d
com.apple.quarantine` workaround. v2 will evaluate Apple Developer ID +
notarization.
