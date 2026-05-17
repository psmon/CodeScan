#!/usr/bin/env bash
#
# CodeScan — Linux/macOS direct installer (downloads pre-built release from GitHub).
#
# Downloads the requested CodeScan release asset for the detected RID,
# verifies SHA256 against checksums.txt, installs to ~/.local/bin/codescan,
# and prints PATH guidance.
#
# User data at ~/.codescan/{db,logs,config} is never modified.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install.sh -o install.sh
#   sh install.sh
#
#   # with options:
#   sh install.sh --version 0.3.94 --install-dir "$HOME/.local/bin"
#
# Environment overrides:
#   CODESCAN_VERSION     Release version (default: latest)
#   CODESCAN_INSTALL_DIR Install directory (default: $HOME/.local/bin)
#   CODESCAN_REPO        GitHub repo (default: psmon/CodeScan)

set -eu

VERSION="${CODESCAN_VERSION:-latest}"
INSTALL_DIR="${CODESCAN_INSTALL_DIR:-$HOME/.local/bin}"
REPO="${CODESCAN_REPO:-psmon/CodeScan}"

while [ "$#" -gt 0 ]; do
    case "$1" in
        --version)      VERSION="$2"; shift 2 ;;
        --install-dir)  INSTALL_DIR="$2"; shift 2 ;;
        --repo)         REPO="$2"; shift 2 ;;
        -h|--help)
            sed -n '2,30p' "$0"
            exit 0
            ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

step() { printf '\033[36m==> %s\033[0m\n' "$1"; }
ok()   { printf '    \033[32m%s\033[0m\n' "$1"; }
warn() { printf '    \033[33m%s\033[0m\n' "$1"; }
err()  { printf '    \033[31m%s\033[0m\n' "$1" >&2; }

# 1. Detect OS/arch → RID
os_raw="$(uname -s)"
arch_raw="$(uname -m)"

case "$os_raw" in
    Linux)  os="linux" ;;
    Darwin) os="osx" ;;
    *)      err "Unsupported OS: $os_raw"; exit 1 ;;
esac
case "$arch_raw" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
    *) err "Unsupported architecture: $arch_raw"; exit 1 ;;
esac
RID="${os}-${arch}"
ASSET="codescan-${RID}.tar.gz"

step "Detected platform: $RID"

# 1b. libc warning for Linux
if [ "$os" = "linux" ]; then
    if ! ldd --version 2>&1 | grep -qi 'glibc\|gnu libc'; then
        warn "Non-glibc libc detected. v1 supports glibc only (musl/Alpine not officially supported)."
        warn "If install or runtime fails, please build from source: https://github.com/$REPO"
    fi
fi

# Pick downloader
if command -v curl >/dev/null 2>&1; then
    DL() { curl -fsSL -H 'User-Agent: codescan-installer' -o "$2" "$1"; }
    GET() { curl -fsSL -H 'User-Agent: codescan-installer' "$1"; }
elif command -v wget >/dev/null 2>&1; then
    DL() { wget -q -U 'codescan-installer' -O "$2" "$1"; }
    GET() { wget -q -U 'codescan-installer' -O - "$1"; }
else
    err "Need curl or wget."
    exit 1
fi

# Pick sha256 tool
if command -v sha256sum >/dev/null 2>&1; then
    SHA256() { sha256sum "$1" | awk '{print $1}'; }
elif command -v shasum >/dev/null 2>&1; then
    SHA256() { shasum -a 256 "$1" | awk '{print $1}'; }
else
    warn "No sha256sum/shasum found — checksum verification will be skipped."
    SHA256() { echo ""; }
fi

# 2. Resolve release
step "Resolving release"
if [ "$VERSION" = "latest" ]; then
    API_URL="https://api.github.com/repos/${REPO}/releases/latest"
else
    case "$VERSION" in
        v*) TAG="$VERSION" ;;
        *)  TAG="v${VERSION}" ;;
    esac
    API_URL="https://api.github.com/repos/${REPO}/releases/tags/${TAG}"
fi

API_JSON="$(GET "$API_URL")"
if [ -z "$API_JSON" ]; then
    err "Failed to fetch release info from $API_URL"
    exit 1
fi

# Naive JSON parsing — sufficient for browser_download_url lines.
ASSET_URL="$(printf '%s\n' "$API_JSON" | tr ',' '\n' | grep -F "\"browser_download_url\"" | grep -F "$ASSET" | sed -E 's/.*"(https:[^"]+)".*/\1/' | head -n1)"
SUMS_URL="$( printf '%s\n' "$API_JSON" | tr ',' '\n' | grep -F "\"browser_download_url\"" | grep -F "checksums.txt"     | sed -E 's/.*"(https:[^"]+)".*/\1/' | head -n1)"
RESOLVED_TAG="$(printf '%s\n' "$API_JSON" | tr ',' '\n' | grep -F "\"tag_name\"" | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/' | head -n1)"

if [ -z "$ASSET_URL" ]; then
    err "Asset '$ASSET' not found in release."
    err "API: $API_URL"
    exit 1
fi
ok "Tag: $RESOLVED_TAG"
ok "Asset: $ASSET"

# 3. Download
TMP="$(mktemp -d -t codescan-install.XXXXXX)"
trap 'rm -rf "$TMP"' EXIT

step "Downloading asset"
DL "$ASSET_URL" "$TMP/$ASSET"
ok "Downloaded to $TMP/$ASSET"

# 4. Verify SHA256
step "Verifying checksum"
if [ -z "$SUMS_URL" ]; then
    warn "checksums.txt not found in release — skipping verification (NOT RECOMMENDED)."
else
    DL "$SUMS_URL" "$TMP/checksums.txt"
    EXPECTED="$(grep -F "$ASSET" "$TMP/checksums.txt" | awk '{print $1}' | head -n1)"
    if [ -z "$EXPECTED" ]; then
        err "Could not find $ASSET entry in checksums.txt"
        exit 1
    fi
    ACTUAL="$(SHA256 "$TMP/$ASSET")"
    if [ -n "$ACTUAL" ] && [ "$ACTUAL" != "$EXPECTED" ]; then
        err "Checksum mismatch!"
        err "  expected: $EXPECTED"
        err "  actual:   $ACTUAL"
        exit 1
    fi
    ok "SHA256 verified: ${ACTUAL:-<skipped>}"
fi

# 5. Extract & install
step "Installing to $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
tar -xzf "$TMP/$ASSET" -C "$TMP"
SRC_BIN="$(find "$TMP" -type f -name codescan -not -path "$TMP/$ASSET" | head -n1)"
if [ -z "$SRC_BIN" ]; then
    err "codescan binary not found inside $ASSET"
    exit 1
fi
install -m 0755 "$SRC_BIN" "$INSTALL_DIR/codescan"
# Optional companions
for f in VERSION README.md LICENSE; do
    EXTRA="$(find "$TMP" -type f -name "$f" -not -path "$TMP/$ASSET" | head -n1 || true)"
    [ -n "$EXTRA" ] && cp "$EXTRA" "$INSTALL_DIR/" || true
done
ok "Installed: $INSTALL_DIR/codescan"

# 6. PATH guidance (do not modify user's rc automatically — print instructions).
case ":$PATH:" in
    *":$INSTALL_DIR:"*)
        PATH_OK=1
        ;;
    *)
        PATH_OK=0
        ;;
esac

# 7. Verify
step "Verifying install"
if "$INSTALL_DIR/codescan" --version >/dev/null 2>&1; then
    "$INSTALL_DIR/codescan" --version
    ok "codescan is working"
else
    warn "codescan --version failed (binary may still work via other commands)"
fi

echo ""
echo "============================================"
echo "  CodeScan ${RESOLVED_TAG#v} installed"
echo "============================================"
echo ""
echo "  Install dir : $INSTALL_DIR"
echo "  Data dir    : ~/.codescan/ (preserved across installs)"
echo ""

if [ "$PATH_OK" -eq 0 ]; then
    echo "  PATH does NOT contain $INSTALL_DIR yet."
    echo "  Add this line to your shell rc (~/.bashrc or ~/.zshrc):"
    echo ""
    echo "      export PATH=\"$INSTALL_DIR:\$PATH\""
    echo ""
    echo "  Then reload: source ~/.bashrc   (or open a new terminal)"
else
    echo "  Try: codescan --help"
fi
echo ""
