#!/bin/bash

# Test script to verify dotnet.js fingerprinting works correctly
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$PROJECT_DIR/Uno.Wasm.Tests.Fingerprint.csproj"

echo "========================================="
echo "Testing dotnet.js Fingerprinting"
echo "========================================="
echo ""

# Clean previous outputs
echo "🧹 Cleaning previous outputs..."
dotnet clean "$PROJECT_FILE" --configuration Release > /dev/null 2>&1 || true
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"

# Test 1: Build
echo ""
echo "📦 Test 1: Build scenario"
echo "----------------------------------------"
dotnet build "$PROJECT_FILE" --configuration Release

BUILD_OUTPUT="$PROJECT_DIR/bin/Release/net10.0"

# Find uno-config.js (may be in a package subdirectory)
BUILD_CONFIG=$(find "$BUILD_OUTPUT/wwwroot" -name "uno-config.js" 2>/dev/null | head -1)

if [ ! -f "$BUILD_CONFIG" ]; then
    echo -e "${RED}❌ FAIL: uno-config.js not found in build output${NC}"
    echo "Searched in: $BUILD_OUTPUT/wwwroot"
    exit 1
fi

echo "Found config at: $BUILD_CONFIG"

# Extract fingerprint from uno-config.js
# Pattern: config.dotnet_js_filename = "dotnet.{fingerprint}.js";
# Use sed for portability (works on Linux, macOS, and Windows Git Bash)
BUILD_FINGERPRINT=$(sed -n 's/.*dotnet_js_filename.*"dotnet\.\([a-z0-9]*\)\.js".*/\1/p' "$BUILD_CONFIG" | head -1)

if [ -z "$BUILD_FINGERPRINT" ]; then
    echo -e "${RED}❌ FAIL: No fingerprint found in build uno-config.js${NC}"
    cat "$BUILD_CONFIG"
    exit 1
fi

echo -e "${GREEN}✓ Found fingerprint in build config: $BUILD_FINGERPRINT${NC}"

# Verify the actual dotnet.js file exists
BUILD_DOTNET_JS="$BUILD_OUTPUT/wwwroot/_framework/dotnet.$BUILD_FINGERPRINT.js"
if [ ! -f "$BUILD_DOTNET_JS" ]; then
    echo -e "${RED}❌ FAIL: dotnet.$BUILD_FINGERPRINT.js not found in build output${NC}"
    echo "Files in _framework:"
    ls -la "$BUILD_OUTPUT/wwwroot/_framework/" | grep dotnet
    exit 1
fi

echo -e "${GREEN}✓ Verified dotnet.$BUILD_FINGERPRINT.js exists in build output${NC}"

# Test 2: Publish
echo ""
echo "📤 Test 2: Publish scenario"
echo "----------------------------------------"
PUBLISH_DIR="$PROJECT_DIR/bin/Release/net10.0/publish"
dotnet publish "$PROJECT_FILE" --configuration Release

# Find uno-config.js in publish output (may be in a package subdirectory)
PUBLISH_CONFIG=$(find "$PUBLISH_DIR/wwwroot" -name "uno-config.js" 2>/dev/null | head -1)

if [ ! -f "$PUBLISH_CONFIG" ]; then
    echo -e "${RED}❌ FAIL: uno-config.js not found in publish output${NC}"
    echo "Searched in: $PUBLISH_DIR/wwwroot"
    exit 1
fi

echo "Found config at: $PUBLISH_CONFIG"

# Extract fingerprint from published uno-config.js
# Pattern: config.dotnet_js_filename = "dotnet.{fingerprint}.js";
# Use sed for portability (works on Linux, macOS, and Windows Git Bash)
PUBLISH_FINGERPRINT=$(sed -n 's/.*dotnet_js_filename.*"dotnet\.\([a-z0-9]*\)\.js".*/\1/p' "$PUBLISH_CONFIG" | head -1)

if [ -z "$PUBLISH_FINGERPRINT" ]; then
    echo -e "${RED}❌ FAIL: No fingerprint found in publish uno-config.js${NC}"
    cat "$PUBLISH_CONFIG"
    exit 1
fi

echo -e "${GREEN}✓ Found fingerprint in publish config: $PUBLISH_FINGERPRINT${NC}"

# Verify the actual dotnet.js file exists in publish output
PUBLISH_DOTNET_JS="$PUBLISH_DIR/wwwroot/_framework/dotnet.$PUBLISH_FINGERPRINT.js"
if [ ! -f "$PUBLISH_DOTNET_JS" ]; then
    echo -e "${RED}❌ FAIL: dotnet.$PUBLISH_FINGERPRINT.js not found in publish output${NC}"
    echo "Files in _framework:"
    ls -la "$PUBLISH_DIR/wwwroot/_framework/" | grep dotnet
    exit 1
fi

echo -e "${GREEN}✓ Verified dotnet.$PUBLISH_FINGERPRINT.js exists in publish output${NC}"

# Test 3: Verify no duplicate fingerprints in config
echo ""
echo "🔍 Test 3: Check for duplicate/conflicting fingerprints"
echo "----------------------------------------"
PUBLISH_CONFIG_CONTENT=$(cat "$PUBLISH_CONFIG")
DOTNET_JS_REFERENCES=$(echo "$PUBLISH_CONFIG_CONTENT" | grep -o 'dotnet\.[a-z0-9]*\.js' | sort | uniq)
REFERENCE_COUNT=$(echo "$DOTNET_JS_REFERENCES" | wc -l)

if [ "$REFERENCE_COUNT" -gt 1 ]; then
    echo -e "${RED}❌ FAIL: Multiple different dotnet.js references found:${NC}"
    echo "$DOTNET_JS_REFERENCES"
    exit 1
fi

echo -e "${GREEN}✓ Only one consistent dotnet.js reference found${NC}"

# Test 4: Verify no leftover placeholder patterns
echo ""
echo "🔍 Test 4: Check for placeholder patterns"
echo "----------------------------------------"
if grep -q '#\[\.{fingerprint}\]' "$PUBLISH_CONFIG"; then
    echo -e "${RED}❌ FAIL: Placeholder pattern still present in config${NC}"
    grep '#\[\.{fingerprint}\]' "$PUBLISH_CONFIG"
    exit 1
fi

echo -e "${GREEN}✓ No placeholder patterns found${NC}"

# Test 4b: Verify stale compressed uno-config.js files are removed
echo ""
echo "🗜️  Test 4b: Check for stale compressed uno-config.js files"
echo "----------------------------------------"
if [ -f "$PUBLISH_CONFIG.gz" ]; then
    echo -e "${RED}❌ FAIL: Stale compressed file exists: $PUBLISH_CONFIG.gz${NC}"
    echo "  The .gz file was generated before the fingerprint update and contains the old"
    echo "  dotnet.js reference. The web server would serve this stale version via content"
    echo "  negotiation, causing a fingerprint mismatch at runtime."
    exit 1
fi

if [ -f "$PUBLISH_CONFIG.br" ]; then
    echo -e "${RED}❌ FAIL: Stale compressed file exists: $PUBLISH_CONFIG.br${NC}"
    echo "  The .br file was generated before the fingerprint update and contains the old"
    echo "  dotnet.js reference. The web server would serve this stale version via content"
    echo "  negotiation, causing a fingerprint mismatch at runtime."
    exit 1
fi

echo -e "${GREEN}✓ No stale compressed uno-config.js files found${NC}"

# Test 5: Nested publish scenario (WasmBuildingForNestedPublish=true must skip fingerprint targets)
echo ""
echo "🔄 Test 5: Nested publish scenario (WasmBuildingForNestedPublish=true)"
echo "----------------------------------------"
# The .NET WASM SDK's inner 'WasmNestedPublishApp' target invokes MSBuild with
# WasmBuildingForNestedPublish=true, causing Publish (and its AfterTargets hooks)
# to run inside the nested build with PublishDir pointing to an intermediate
# directory where dotnet.js hasn't been fingerprinted yet.
# Our targets must be skipped in that context to avoid UNOWASM001/UNOWASM002.
NESTED_LOG=$(mktemp)
set +e
dotnet publish "$PROJECT_FILE" --configuration Release \
    -p:WasmBuildingForNestedPublish=true 2>&1 | tee "$NESTED_LOG"
NESTED_EXIT=${PIPESTATUS[0]}
set -e

if grep -qE "error UNOWASM001|error UNOWASM002" "$NESTED_LOG"; then
    echo -e "${RED}❌ FAIL: Fingerprint error emitted during nested publish${NC}"
    echo "Fingerprint targets must be skipped when WasmBuildingForNestedPublish=true"
    grep -E "UNOWASM001|UNOWASM002" "$NESTED_LOG"
    rm -f "$NESTED_LOG"
    exit 1
fi

rm -f "$NESTED_LOG"

if [ "$NESTED_EXIT" -ne 0 ]; then
    echo -e "${RED}❌ FAIL: dotnet publish with WasmBuildingForNestedPublish=true failed${NC}"
    exit "$NESTED_EXIT"
fi

echo -e "${GREEN}✓ Nested publish completed without fingerprint errors (targets correctly skipped)${NC}"

# Test 6: Verify _UnoUpdateDotnetJsFingerprintPublishOutput fires after WasmTriggerPublishApp
# The .NET SDK's WasmTriggerPublishApp (AfterTargets="Publish") invokes a nested MSBuild that
# performs AOT/relinking, producing a new dotnet.js with a different fingerprint. Our target
# must run AFTER WasmTriggerPublishApp so that it reads the final dotnet.js fingerprint.
# The fix is AfterTargets="Publish;WasmTriggerPublishApp" which ensures correct ordering.
echo ""
echo "⏱️  Test 6: Verify fingerprint update fires after WasmTriggerPublishApp (correct AfterTargets hook)"
echo "----------------------------------------"
ORDER_LOG=$(mktemp)
set +e
dotnet publish "$PROJECT_FILE" --configuration Release -verbosity:detailed > "$ORDER_LOG" 2>&1
ORDER_EXIT=$?
set -e

if [ "$ORDER_EXIT" -ne 0 ]; then
    echo -e "${RED}❌ FAIL: dotnet publish failed${NC}"
    rm -f "$ORDER_LOG"
    exit "$ORDER_EXIT"
fi

# Check that the target did not emit the "ran too early" warning (files not present yet)
if grep -q "Could not find dotnet.js file in publish output" "$ORDER_LOG"; then
    echo -e "${RED}❌ FAIL: _UnoUpdateDotnetJsFingerprintPublishOutput ran before dotnet.js was placed in publish dir${NC}"
    grep "Could not find dotnet.js" "$ORDER_LOG"
    rm -f "$ORDER_LOG"
    exit 1
fi

# Verify the fingerprint update message is present and fires AFTER WasmTriggerPublishApp.
# If WasmTriggerPublishApp is present in the log, our target must come after it.
# If WasmTriggerPublishApp is not present (non-AOT), just verify our target ran.
UPDATE_LINE=$(grep -n "Updated uno-config.js with dotnet.js fingerprint" "$ORDER_LOG" | head -1 | cut -d: -f1)
WASM_TRIGGER_LINE=$(grep -n 'WasmTriggerPublishApp' "$ORDER_LOG" | tail -1 | cut -d: -f1)

rm -f "$ORDER_LOG"

if [ -z "$UPDATE_LINE" ]; then
    echo -e "${RED}❌ FAIL: 'Updated uno-config.js with dotnet.js fingerprint' message not found${NC}"
    echo "  _UnoUpdateDotnetJsFingerprintPublishOutput may not have run or emitted no output"
    exit 1
fi

echo "  Fingerprint update log line: $UPDATE_LINE"

if [ -z "$WASM_TRIGGER_LINE" ]; then
    echo -e "${YELLOW}ℹ  Note: 'WasmTriggerPublishApp' not found in log (non-AOT build or SDK version difference)${NC}"
    echo -e "${GREEN}✓ Fingerprint update ran; WasmTriggerPublishApp ordering check skipped${NC}"
else
    echo "  WasmTriggerPublishApp log line: $WASM_TRIGGER_LINE"
    if [ "$UPDATE_LINE" -gt "$WASM_TRIGGER_LINE" ]; then
        echo -e "${GREEN}✓ Fingerprint update fires AFTER WasmTriggerPublishApp (AfterTargets=\"Publish;WasmTriggerPublishApp\")${NC}"
    else
        echo -e "${RED}❌ FAIL: Fingerprint update fires BEFORE WasmTriggerPublishApp (line $UPDATE_LINE <= $WASM_TRIGGER_LINE)${NC}"
        echo "  _UnoUpdateDotnetJsFingerprintPublishOutput must run after WasmTriggerPublishApp"
        echo "  so it reads the final dotnet.js fingerprint after AOT/relinking."
        echo "  Fix: use AfterTargets=\"Publish;WasmTriggerPublishApp\" in Uno.Wasm.Bootstrap.targets"
        exit 1
    fi
fi

# Summary
echo ""
echo "========================================="
echo -e "${GREEN}✅ ALL TESTS PASSED${NC}"
echo "========================================="
echo ""
echo "Summary:"
echo "  Build fingerprint:   $BUILD_FINGERPRINT"
echo "  Publish fingerprint: $PUBLISH_FINGERPRINT"
echo ""

if [ "$BUILD_FINGERPRINT" != "$PUBLISH_FINGERPRINT" ]; then
    echo -e "${YELLOW}ℹ Note: Fingerprints differ between build and publish (expected due to AOT/optimizations)${NC}"
fi

exit 0
