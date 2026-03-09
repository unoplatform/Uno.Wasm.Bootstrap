#!/bin/bash

# Test script to verify WebWorker fork functionality works correctly.
# Publishes the WorkerFork.App, serves it with COEP/COOP headers, then
# launches headless Chrome to confirm the app forks a worker and exchanges
# messages end-to-end.
#
# Usage: ./test-worker-fork.sh [artifacts-dir]

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

APP_DIR="$REPO_ROOT/src/Uno.Wasm.Tests.WorkerFork.App"
PUBLISH_DIR="$APP_DIR/bin/Release/net10.0/publish"
WWWROOT="$PUBLISH_DIR/wwwroot"

echo "========================================="
echo "Worker Fork Test"
echo "========================================="
echo ""

# -------------------------------------------------------------------
# Step 1: Publish WorkerFork test app
# -------------------------------------------------------------------
echo "Step 1: Publishing WorkerFork test app..."
echo "-----------------------------------------"
cd "$APP_DIR"
dotnet clean -c Release > /dev/null 2>&1 || true
dotnet publish -c Release /m:1 \
    /bl:"$ARTIFACTS_DIR/WorkerFork-linux.binlog"

if [ ! -d "$WWWROOT" ]; then
    echo -e "${RED}FAIL: Published output not found at $WWWROOT${NC}"
    exit 1
fi

echo -e "${GREEN}Publish succeeded.${NC}"
echo ""

# -------------------------------------------------------------------
# Step 2: Serve the app and run Puppeteer validation
# -------------------------------------------------------------------
echo "Step 2: Runtime validation..."
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
node validate-worker-fork.js "$ARTIFACTS_DIR" "http://localhost:8000/"
RESULT=$?

echo ""
echo "-----------------------------------------"

if [ "$RESULT" -ne 0 ]; then
    echo -e "${RED}FAIL: Worker fork runtime validation failed.${NC}"
    exit 1
fi

echo ""
echo "========================================="
echo -e "${GREEN}ALL TESTS PASSED${NC}"
echo "========================================="

exit 0
