# typed: false
# frozen_string_literal: true

# CodeScan Homebrew formula (template).
#
# v1 distribution lives in the `psmon/homebrew-codescan` tap:
#
#     brew tap psmon/codescan
#     brew install codescan
#
# The release workflow (or a manual update) rewrites VERSION + the two SHA256
# placeholders before pushing this file to the tap repo at
# `Formula/codescan.rb`.

class Codescan < Formula
  desc "CLI/TUI/GUI source-code scanner with FTS5 search and git blame"
  homepage "https://github.com/psmon/CodeScan"
  version "VERSION_PLACEHOLDER"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/psmon/CodeScan/releases/download/v#{version}/codescan-osx-arm64.tar.gz"
      sha256 "SHA256_OSX_ARM64_PLACEHOLDER"
    end
    on_intel do
      url "https://github.com/psmon/CodeScan/releases/download/v#{version}/codescan-osx-x64.tar.gz"
      sha256 "SHA256_OSX_X64_PLACEHOLDER"
    end
  end

  def install
    bin.install "codescan/codescan"
    pkgshare.install "codescan/VERSION" if File.exist?("codescan/VERSION")
    doc.install "codescan/README.md" if File.exist?("codescan/README.md")
  end

  def caveats
    <<~EOS
      CodeScan stores user data under ~/.codescan/ (db, logs, config).
      This directory is preserved across upgrades and uninstalls.

      The v1 binary is not notarized. If macOS Gatekeeper blocks first run:
        xattr -d com.apple.quarantine #{bin}/codescan
      or allow it from System Settings → Privacy & Security.
    EOS
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/codescan --version")
  end
end
