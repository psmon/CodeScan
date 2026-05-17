#!/usr/bin/env bash
#
# Rewrites packaging/homebrew/codescan.rb for a specific release and emits the
# result to stdout. The release workflow pipes this into the tap repo at
# `Formula/codescan.rb`.
#
# Usage:
#   ./update-formula.sh <version> <checksums.txt-path>
#
# Example:
#   ./update-formula.sh 0.3.94 ./dist/checksums.txt > /tmp/codescan.rb

set -eu

VERSION="${1:?version required (e.g. 0.3.94)}"
SUMS_FILE="${2:?path to checksums.txt required}"
FORMULA_DIR="$(cd "$(dirname "$0")" && pwd)"
FORMULA="${FORMULA_DIR}/codescan.rb"

extract_sha() {
    local asset="$1"
    grep -F "$asset" "$SUMS_FILE" | awk '{print $1}' | head -n1
}

SHA_ARM64="$(extract_sha codescan-osx-arm64.tar.gz)"
SHA_X64="$(  extract_sha codescan-osx-x64.tar.gz)"

if [ -z "$SHA_ARM64" ] || [ -z "$SHA_X64" ]; then
    echo "Missing SHA256 for one or more osx assets in $SUMS_FILE" >&2
    exit 1
fi

sed \
    -e "s|VERSION_PLACEHOLDER|${VERSION}|g" \
    -e "s|SHA256_OSX_ARM64_PLACEHOLDER|${SHA_ARM64}|g" \
    -e "s|SHA256_OSX_X64_PLACEHOLDER|${SHA_X64}|g" \
    "$FORMULA"
