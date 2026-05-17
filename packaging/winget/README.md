# winget packaging

Portable manifest scaffolding for `psmon.CodeScan`.

## Files

```
manifests/p/psmon/CodeScan/0.0.0/
├── psmon.CodeScan.installer.yaml      # installer (portable, x64)
├── psmon.CodeScan.locale.en-US.yaml   # default locale metadata
└── psmon.CodeScan.yaml                # version manifest
```

The `0.0.0` directory is a **template path** — the release workflow copies these
files into a version-specific path under the user's `microsoft/winget-pkgs` fork
before opening a PR.

## Per-release substitutions

The release workflow (or a manual submission) must rewrite the following:

| Token       | Source |
|-------------|--------|
| `0.0.0` (PackageVersion + directory) | `version.txt` at release time |
| `{VERSION}` | same |
| `{SHA256}`  | SHA256 of `codescan-win-x64.zip` from `checksums.txt` |
| `ReleaseDate: 1970-01-01` | release publication date (`YYYY-MM-DD`) |

## v1 submission flow (manual)

1. Publish a GitHub Release (via `.github/workflows/release.yml`).
2. Note the `codescan-win-x64.zip` SHA256 from `checksums.txt`.
3. Fork `microsoft/winget-pkgs`.
4. Copy this directory to `manifests/p/psmon/CodeScan/<VERSION>/` in the fork.
5. Rewrite the tokens above.
6. Run `winget validate` locally:
   ```powershell
   winget validate --manifest manifests/p/psmon/CodeScan/<VERSION>
   ```
7. Open a PR to `microsoft/winget-pkgs`.

## v1.x automation

`.github/workflows/release.yml` contains an `if: false`-gated
`publish-winget` job using `vedantmgoyal9/winget-releaser`.
Set `secrets.WINGET_TOKEN` (a PAT with PR-create rights on the fork) and flip
`if:` to `true` once one manual submission has been accepted.
