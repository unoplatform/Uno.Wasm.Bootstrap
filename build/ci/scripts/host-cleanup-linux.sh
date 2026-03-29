#!/usr/bin/env bash
# Reclaims disk space on Linux CI agents by removing pre-installed
# software that is not needed for the build.
#
# This list is based on what the base image contains and
# may need to be adjusted as new software gets installed.
# Use the `du` command to determine what can be uninstalled.

# Use sudo only when available (containers may not have it)
if command -v sudo >/dev/null 2>&1; then
  SUDO="sudo"
else
  SUDO=""
fi

echo "Disk space before cleanup:"
df -h /

rm -rf ~/.cargo ~/.rustup ~/.dotnet

$SUDO rm -rf /usr/share/swift || true
$SUDO rm -rf /opt/microsoft/msedge || true
$SUDO rm -rf /usr/local/.ghcup || true
$SUDO rm -rf /usr/lib/mono || true
$SUDO rm -rf /usr/local/lib/android || true
$SUDO rm -rf /opt/ghc || true
$SUDO rm -rf /opt/hostedtoolcache/CodeQL || true

$SUDO snap remove lxd || true
$SUDO snap remove core20 || true
$SUDO apt-get purge -y snapd || true

echo "Disk space after cleanup:"
df -h /
