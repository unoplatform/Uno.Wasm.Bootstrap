#!/bin/bash

# Test script to verify memory profiler export produces valid speedscope and PerfView JSON.
# Locates the compiled uno-bootstrap.js, runs the Node.js validation script, and
# copies output files to an artifacts directory.
#
# Usage: ./test-memory-profiler-export.sh [artifacts-dir]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACTS_DIR="${1:-$SCRIPT_DIR/artifacts}"

echo "========================================="
echo "Memory Profiler Export Format Test"
echo "========================================="
echo ""

# Locate the compiled uno-bootstrap.js from the Bootstrap build output.
# After 'dotnet build/msbuild' on Uno.Wasm.Bootstrap, TypeScript is compiled
# into src/Uno.Wasm.Bootstrap/obj/uno-bootstrap.js.
BOOTSTRAP_JS="$SCRIPT_DIR/../Uno.Wasm.Bootstrap/obj/uno-bootstrap.js"

if [ ! -f "$BOOTSTRAP_JS" ]; then
    echo -e "${RED}ERROR: Compiled uno-bootstrap.js not found at:${NC}"
    echo "  $BOOTSTRAP_JS"
    echo ""
    echo "Build the Bootstrap project first:"
    echo "  dotnet build src/Uno.Wasm.Bootstrap/Uno.Wasm.Bootstrap.csproj"
    exit 1
fi

echo "Found compiled JS: $BOOTSTRAP_JS"

# Check that downloadSnapshot is present (i.e. the TS was compiled with the export methods)
if ! grep -q "downloadSnapshot" "$BOOTSTRAP_JS"; then
    echo -e "${RED}ERROR: downloadSnapshot not found in compiled JS.${NC}"
    echo "  The TypeScript may not have been recompiled. Rebuild the Bootstrap project."
    exit 1
fi

echo "Confirmed downloadSnapshot method present."
echo ""

# Run the Node.js validation script
echo "Running export format validation..."
echo "-----------------------------------------"

VALIDATE_SCRIPT="$SCRIPT_DIR/validate-export-formats.mjs"

if [ ! -f "$VALIDATE_SCRIPT" ]; then
    echo -e "${RED}ERROR: Validation script not found: $VALIDATE_SCRIPT${NC}"
    exit 1
fi

mkdir -p "$ARTIFACTS_DIR"

node "$VALIDATE_SCRIPT" "$BOOTSTRAP_JS" "$ARTIFACTS_DIR"
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
