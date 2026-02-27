#!/bin/bash

# Test script to verify memory profiler export produces valid speedscope and PerfView JSON.
# Serves the published RayTracer app (built with WasmShellEnableWasmMemoryProfiler=true),
# loads it in headless Chrome via Puppeteer, calls downloadSnapshot for both formats,
# validates the JSON output, and writes artifact files.
#
# Usage: ./test-memory-profiler-export.sh [artifacts-dir]

set -e

cleanup() {
    kill %% 2>/dev/null || true
}
trap cleanup EXIT

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ARTIFACTS_DIR="${1:-$SCRIPT_DIR/artifacts}"

echo "========================================="
echo "Memory Profiler Export Format Test"
echo "========================================="
echo ""

# Locate the published RayTracer output
RAYTRACER_WWWROOT="$REPO_ROOT/src/Uno.Wasm.Sample.RayTracer/bin/Release/net10.0/publish/wwwroot"

if [ ! -d "$RAYTRACER_WWWROOT" ]; then
    echo -e "${RED}ERROR: Published RayTracer not found at:${NC}"
    echo "  $RAYTRACER_WWWROOT"
    echo ""
    echo "Build with:"
    echo "  dotnet publish -c Release src/Uno.Wasm.Sample.RayTracer /p:WasmShellEnableWasmMemoryProfiler=true"
    exit 1
fi

echo "Found published RayTracer: $RAYTRACER_WWWROOT"

# Install dotnet-serve (same approach as run-tests.sh)
dotnet tool uninstall dotnet-serve -g 2>/dev/null || true
dotnet tool uninstall dotnet-serve --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
dotnet tool install dotnet-serve --version 1.10.175 --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
export PATH="$PATH:$REPO_ROOT/build/tools"

# Start the server
echo "Starting HTTP server..."
cd "$RAYTRACER_WWWROOT"
dotnet serve -p 8000 -c -b \
    -h "Cross-Origin-Embedder-Policy: require-corp" \
    -h "Cross-Origin-Opener-Policy: same-origin" &
sleep 5

# Install Puppeteer dependencies
echo ""
echo "Installing test dependencies..."
cd "$SCRIPT_DIR"
npm install

# Run the Puppeteer-based validation
echo ""
echo "Running export format validation..."
echo "-----------------------------------------"

mkdir -p "$ARTIFACTS_DIR"

node validate-export-formats.js "$ARTIFACTS_DIR" "http://localhost:8000/"
RESULT=$?

echo ""
echo "-----------------------------------------"

if [ "$RESULT" -ne 0 ]; then
    echo -e "${RED}FAIL: Memory profiler export format validation failed.${NC}"
    exit 1
fi

# Verify output files
SPEEDSCOPE_FILE="$ARTIFACTS_DIR/test-output.speedscope.json"
PERFVIEW_FILE="$ARTIFACTS_DIR/test-output.PerfView.json"

if [ ! -f "$SPEEDSCOPE_FILE" ]; then
    echo -e "${RED}FAIL: Expected output file missing: $SPEEDSCOPE_FILE${NC}"
    exit 1
fi

if [ ! -f "$PERFVIEW_FILE" ]; then
    echo -e "${RED}FAIL: Expected output file missing: $PERFVIEW_FILE${NC}"
    exit 1
fi

echo ""
echo "========================================="
echo -e "${GREEN}ALL TESTS PASSED${NC}"
echo "========================================="
echo ""
echo "Output files:"
echo "  $SPEEDSCOPE_FILE"
echo "  $PERFVIEW_FILE"

exit 0
