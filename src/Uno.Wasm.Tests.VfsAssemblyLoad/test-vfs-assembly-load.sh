#!/bin/bash

# Test script to verify VFS framework assembly loading works correctly.
# Publishes the VfsAssemblyLoad.App with WasmShellVfsFrameworkAssemblyLoad=true
# and WasmShellVfsFrameworkAssemblyLoadCleanup=true (set in its csproj), validates
# the generated config, then launches headless Chrome to confirm the app runs and
# VFS cleanup occurs.
#
# Usage: ./test-vfs-assembly-load.sh [artifacts-dir]

set -e

cleanup() {
    kill %% 2>/dev/null || true
}
trap cleanup EXIT

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${BUILD_SOURCESDIRECTORY:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
ARTIFACTS_DIR="${1:-$SCRIPT_DIR/artifacts}"
mkdir -p "$ARTIFACTS_DIR"

VFS_APP_DIR="$REPO_ROOT/src/Uno.Wasm.Tests.VfsAssemblyLoad.App"
PUBLISH_DIR="$VFS_APP_DIR/bin/Release/net10.0/publish"
WWWROOT="$PUBLISH_DIR/wwwroot"

echo "========================================="
echo "VFS Framework Assembly Load Test"
echo "========================================="
echo ""

# -------------------------------------------------------------------
# Step 1: Publish VFS test app (VFS loading + cleanup enabled in csproj)
# -------------------------------------------------------------------
echo "Step 1: Publishing VFS test app..."
echo "-----------------------------------------"
cd "$VFS_APP_DIR"
dotnet clean -c Release > /dev/null 2>&1 || true
dotnet publish -c Release /m:1 \
    /bl:"$ARTIFACTS_DIR/VfsAssemblyLoad-linux.binlog"

if [ ! -d "$WWWROOT" ]; then
    echo -e "${RED}FAIL: Published output not found at $WWWROOT${NC}"
    exit 1
fi

echo -e "${GREEN}Publish succeeded.${NC}"
echo ""

# -------------------------------------------------------------------
# Step 2: Validate uno-config.js contains the VFS properties
# -------------------------------------------------------------------
echo "Step 2: Validating uno-config.js..."
echo "-----------------------------------------"

CONFIG_JS=$(find "$WWWROOT" -name "uno-config.js" 2>/dev/null | head -1)
if [ ! -f "$CONFIG_JS" ]; then
    echo -e "${RED}FAIL: uno-config.js not found in $WWWROOT${NC}"
    exit 1
fi

# Check that VFS assembly loading is enabled in config
if ! grep -q 'uno_vfs_framework_assembly_load = true' "$CONFIG_JS"; then
    echo -e "${RED}FAIL: uno_vfs_framework_assembly_load not set to true in uno-config.js${NC}"
    grep 'uno_vfs_framework_assembly_load' "$CONFIG_JS" || echo "  (property not found)"
    exit 1
fi
echo -e "${GREEN}  uno_vfs_framework_assembly_load = true${NC}"

# Check that VFS cleanup is enabled in config
if ! grep -q 'uno_vfs_framework_assembly_load_cleanup = true' "$CONFIG_JS"; then
    echo -e "${RED}FAIL: uno_vfs_framework_assembly_load_cleanup not set to true in uno-config.js${NC}"
    grep 'uno_vfs_framework_assembly_load_cleanup' "$CONFIG_JS" || echo "  (property not found)"
    exit 1
fi
echo -e "${GREEN}  uno_vfs_framework_assembly_load_cleanup = true${NC}"

echo ""

# -------------------------------------------------------------------
# Step 3: Serve the app and run Puppeteer validation
# -------------------------------------------------------------------
echo "Step 3: Runtime validation..."
echo "-----------------------------------------"

# Install dotnet-serve
dotnet tool uninstall dotnet-serve -g 2>/dev/null || true
dotnet tool uninstall dotnet-serve --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
dotnet tool install dotnet-serve --version 1.10.175 --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
export PATH="$PATH:$REPO_ROOT/build/tools"

# Start the server
echo "Starting HTTP server..."
cd "$WWWROOT"
dotnet serve -p 8000 -c -b \
    -h "Cross-Origin-Embedder-Policy: require-corp" \
    -h "Cross-Origin-Opener-Policy: same-origin" &
sleep 5

# Install Puppeteer dependencies
cd "$SCRIPT_DIR"
npm install

# Run the Puppeteer-based validation
echo ""
echo "Running runtime validation..."
node validate-vfs-runtime.js "$ARTIFACTS_DIR" "http://localhost:8000/"
RESULT=$?

echo ""
echo "-----------------------------------------"

if [ "$RESULT" -ne 0 ]; then
    echo -e "${RED}FAIL: VFS assembly load runtime validation failed.${NC}"
    exit 1
fi

echo ""
echo "========================================="
echo -e "${GREEN}ALL TESTS PASSED${NC}"
echo "========================================="

exit 0
