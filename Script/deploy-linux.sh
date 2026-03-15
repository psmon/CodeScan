#!/usr/bin/env bash
#
# CodeScan - Linux/macOS Build & Deploy Script
#
# Builds Release, publishes to ~/.codescan/bin, and adds to user PATH.
#
# Usage (source로 실행하면 현재 세션에 PATH 즉시 반영):
#   source ./Script/deploy-linux.sh
#   source ./Script/deploy-linux.sh /custom/path
#
# 일반 실행도 가능 (새 터미널부터 PATH 반영):
#   ./Script/deploy-linux.sh

set -euo pipefail

DEPLOY_PATH="${1:-$HOME/.codescan/bin}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo "============================================"
echo "  CodeScan - Build & Deploy (Linux/macOS)"
echo "============================================"
echo ""
echo "  Project : $PROJECT_DIR"
echo "  Deploy  : $DEPLOY_PATH"
echo ""

# 1. Clean bin/obj
echo "[1/4] Cleaning..."
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"

# 2. Create deploy dir
mkdir -p "$DEPLOY_PATH"

# 3. Build & Publish (framework-dependent, no trimming)
echo "[2/4] Building Release..."
dotnet publish "$PROJECT_DIR/CodeScan.csproj" \
    -c Release \
    -r linux-x64 \
    -o "$DEPLOY_PATH" \
    --no-self-contained \
    -p:PublishAot=false \
    -p:TrimMode="" \
    -p:IlcOptimizationPreference=""

# 4. Verify
EXE_PATH="$DEPLOY_PATH/codescan"
DLL_PATH="$DEPLOY_PATH/codescan.dll"

if [ -f "$EXE_PATH" ]; then
    FILE_SIZE=$(du -h "$EXE_PATH" | cut -f1)
    echo "[3/4] Built: codescan ($FILE_SIZE)"
    chmod +x "$EXE_PATH"
elif [ -f "$DLL_PATH" ]; then
    FILE_SIZE=$(du -h "$DLL_PATH" | cut -f1)
    echo "[3/4] Built: codescan.dll ($FILE_SIZE) (use: dotnet codescan.dll)"
else
    echo "Build output not found!"
    exit 1
fi

# 5. Add to PATH (persistent)
add_to_path() {
    local shell_rc="$1"
    local export_line="export PATH=\"$DEPLOY_PATH:\$PATH\""

    if [ -f "$shell_rc" ] && grep -qF "$DEPLOY_PATH" "$shell_rc" 2>/dev/null; then
        echo "[4/4] PATH already configured in $(basename "$shell_rc")"
        return
    fi

    if [ -f "$shell_rc" ] || [ "$(basename "$shell_rc")" = ".bashrc" ]; then
        echo "" >> "$shell_rc"
        echo "# CodeScan" >> "$shell_rc"
        echo "$export_line" >> "$shell_rc"
        echo "[4/4] Added to PATH in $(basename "$shell_rc")"
        NEED_SOURCE="$shell_rc"
    fi
}

# Detect shell and update rc file
NEED_SOURCE=""
UPDATED=false
if [ -f "$HOME/.bashrc" ]; then
    add_to_path "$HOME/.bashrc"
    UPDATED=true
fi
if [ -f "$HOME/.zshrc" ]; then
    add_to_path "$HOME/.zshrc"
    UPDATED=true
fi

if [ "$UPDATED" = false ]; then
    # Create .bashrc if neither exists
    add_to_path "$HOME/.bashrc"
fi

# Apply PATH to current session
export PATH="$DEPLOY_PATH:$PATH"

# Source rc file so parent shell (if sourced) gets PATH immediately
if [ -n "$NEED_SOURCE" ]; then
    echo "      Sourcing $(basename "$NEED_SOURCE")..."
    # shellcheck disable=SC1090
    source "$NEED_SOURCE" 2>/dev/null || true
fi

echo ""
echo "============================================"
echo "  Deploy complete!"
echo "============================================"
echo ""

# Version check
if [ -f "$EXE_PATH" ]; then
    "$EXE_PATH" --version 2>/dev/null || true
elif [ -f "$DLL_PATH" ]; then
    dotnet "$DLL_PATH" --version 2>/dev/null || true
fi

echo ""
echo "  Deploy path: $DEPLOY_PATH"
echo "  Data path:   ~/.codescan/db/"
echo "  Logs path:   ~/.codescan/logs/"
echo ""
echo "  Usage (from any directory):"
echo "    codescan --help"
echo "    codescan list ./src --tree --detail"
echo "    codescan search \"HttpClient\""
echo "    codescan tui"
echo ""

# Reload shell so codescan is available immediately
# exec $SHELL replaces the current process with a fresh shell that reads .bashrc
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    # Script was executed (./deploy-linux.sh), not sourced
    echo "  Reloading shell..."
    echo ""
    exec "$SHELL"
else
    # Script was sourced (source ./deploy-linux.sh) - PATH already applied
    echo "  codescan is ready to use!"
    echo ""
fi
