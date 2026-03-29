#!/usr/bin/env bash
# Reclaims disk space on Linux CI agents by removing pre-installed
# software that is not needed for the build.
#
# This list is based on what the base image contains and
# may need to be adjusted as new software gets installed.
# Use the `du` command to determine what can be uninstalled.

# Use sudo only when available and non-interactive (no password prompt)
if command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1; then
  SUDO="sudo -n"
else
  SUDO=""
fi

echo "Disk space before cleanup:"
df -h /

rm -rf ~/.cargo ~/.rustup ~/.dotnet || true

$SUDO rm -rf /usr/share/swift || true
$SUDO rm -rf /opt/microsoft/msedge || true
$SUDO rm -rf /usr/local/.ghcup || true
$SUDO rm -rf /usr/lib/mono || true
$SUDO rm -rf /usr/local/lib/android || true
$SUDO rm -rf /opt/ghc || true
$SUDO rm -rf /opt/hostedtoolcache/CodeQL || true

if command -v snap >/dev/null 2>&1; then
  timeout 60s $SUDO snap remove lxd || true
  timeout 60s $SUDO snap remove core20 || true
fi

if command -v apt-get >/dev/null 2>&1; then
  DEBIAN_FRONTEND=noninteractive timeout 120s $SUDO apt-get purge -y snapd || true
fi

echo "Disk space after cleanup:"
df -h /
