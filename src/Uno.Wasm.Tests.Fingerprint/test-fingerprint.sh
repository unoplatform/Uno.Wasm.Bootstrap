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

# Verify the actual dotnet.js file exists (check both old and .NET 10 layouts)
BUILD_DOTNET_JS="$BUILD_OUTPUT/wwwroot/_framework/dotnet.$BUILD_FINGERPRINT.js"
BUILD_DOTNET_JS_ROOT="$BUILD_OUTPUT/dotnet.js"
if [ -f "$BUILD_DOTNET_JS" ]; then
    echo -e "${GREEN}✓ Verified dotnet.$BUILD_FINGERPRINT.js exists in build output (_framework layout)${NC}"
elif [ -f "$BUILD_DOTNET_JS_ROOT" ]; then
    echo -e "${GREEN}✓ Verified dotnet.js exists in build output root (custom PublishDir layout, fingerprint from hash)${NC}"
else
    echo -e "${RED}❌ FAIL: dotnet.js not found in build output${NC}"
    echo "Checked: $BUILD_DOTNET_JS"
    echo "Checked: $BUILD_DOTNET_JS_ROOT"
    exit 1
fi

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

# Verify the actual dotnet.js file exists in publish output (check both old and .NET 10 layouts)
PUBLISH_DOTNET_JS="$PUBLISH_DIR/wwwroot/_framework/dotnet.$PUBLISH_FINGERPRINT.js"
PUBLISH_DOTNET_JS_ROOT="$PUBLISH_DIR/dotnet.js"
if [ -f "$PUBLISH_DOTNET_JS" ]; then
    echo -e "${GREEN}✓ Verified dotnet.$PUBLISH_FINGERPRINT.js exists in publish output (_framework layout)${NC}"
elif [ -f "$PUBLISH_DOTNET_JS_ROOT" ]; then
    echo -e "${GREEN}✓ Verified dotnet.js exists in publish output root (custom PublishDir layout, fingerprint from hash)${NC}"
else
    echo -e "${RED}❌ FAIL: dotnet.js not found in publish output${NC}"
    echo "Checked: $PUBLISH_DOTNET_JS"
    echo "Checked: $PUBLISH_DOTNET_JS_ROOT"
    exit 1
fi

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

# Test 6: Build with fingerprinting disabled via WasmShellEnableDotnetJsFingerprinting=false
echo ""
echo "🚫 Test 6: Build with WasmShellEnableDotnetJsFingerprinting=false"
echo "----------------------------------------"
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"
dotnet build "$PROJECT_FILE" --configuration Release -p:WasmShellEnableDotnetJsFingerprinting=false

BUILD_OUTPUT_NOFP="$PROJECT_DIR/bin/Release/net10.0"
BUILD_CONFIG_NOFP=$(find "$BUILD_OUTPUT_NOFP/wwwroot" -name "uno-config.js" 2>/dev/null | head -1)

if [ ! -f "$BUILD_CONFIG_NOFP" ]; then
    echo -e "${RED}❌ FAIL: uno-config.js not found in build output${NC}"
    echo "Searched in: $BUILD_OUTPUT_NOFP/wwwroot"
    exit 1
fi

# When fingerprinting is disabled, config should reference plain "dotnet.js" (no hash)
NOFP_BUILD_FINGERPRINT=$(sed -n 's/.*dotnet_js_filename.*"dotnet\.\([a-z0-9]*\)\.js".*/\1/p' "$BUILD_CONFIG_NOFP" | head -1)

if [ -n "$NOFP_BUILD_FINGERPRINT" ]; then
    echo -e "${RED}❌ FAIL: Fingerprint found in build uno-config.js despite WasmShellEnableDotnetJsFingerprinting=false${NC}"
    echo "  Unexpected fingerprint: $NOFP_BUILD_FINGERPRINT"
    cat "$BUILD_CONFIG_NOFP"
    exit 1
fi

echo -e "${GREEN}✓ Build config does not contain fingerprinted dotnet.js reference (fingerprinting disabled)${NC}"

# Test 7: Publish with fingerprinting disabled via WasmShellEnableDotnetJsFingerprinting=false
echo ""
echo "🚫 Test 7: Publish with WasmShellEnableDotnetJsFingerprinting=false"
echo "----------------------------------------"
PUBLISH_DIR_NOFP="$PROJECT_DIR/bin/Release/net10.0/publish"
dotnet publish "$PROJECT_FILE" --configuration Release -p:WasmShellEnableDotnetJsFingerprinting=false

PUBLISH_CONFIG_NOFP=$(find "$PUBLISH_DIR_NOFP/wwwroot" -name "uno-config.js" 2>/dev/null | head -1)

if [ ! -f "$PUBLISH_CONFIG_NOFP" ]; then
    echo -e "${RED}❌ FAIL: uno-config.js not found in publish output${NC}"
    echo "Searched in: $PUBLISH_DIR_NOFP/wwwroot"
    exit 1
fi

# When fingerprinting is disabled, config should reference plain "dotnet.js" (no hash)
NOFP_PUBLISH_FINGERPRINT=$(sed -n 's/.*dotnet_js_filename.*"dotnet\.\([a-z0-9]*\)\.js".*/\1/p' "$PUBLISH_CONFIG_NOFP" | head -1)

if [ -n "$NOFP_PUBLISH_FINGERPRINT" ]; then
    echo -e "${RED}❌ FAIL: Fingerprint found in publish uno-config.js despite WasmShellEnableDotnetJsFingerprinting=false${NC}"
    echo "  Unexpected fingerprint: $NOFP_PUBLISH_FINGERPRINT"
    cat "$PUBLISH_CONFIG_NOFP"
    exit 1
fi

echo -e "${GREEN}✓ Publish config does not contain fingerprinted dotnet.js reference (fingerprinting disabled)${NC}"

# Test 8: Publish with custom absolute PublishDir
# This reproduces the CI scenario where PublishDir is an external absolute path.
# In this layout, dotnet.js lands directly in PublishDir root (not wwwroot/_framework/).
echo ""
echo "📤 Test 8: Publish with custom absolute PublishDir"
echo "----------------------------------------"
CUSTOM_PUBLISH_DIR=$(mktemp -d)
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"

CUSTOM_LOG=$(mktemp)
set +e
dotnet publish "$PROJECT_FILE" --configuration Release \
    -p:PublishDir="$CUSTOM_PUBLISH_DIR" 2>&1 | tee "$CUSTOM_LOG"
CUSTOM_EXIT=${PIPESTATUS[0]}
set -e

if grep -qE "error UNOWASM001|error UNOWASM002" "$CUSTOM_LOG"; then
    echo -e "${RED}❌ FAIL: Fingerprint error emitted with custom PublishDir${NC}"
    grep -E "UNOWASM001|UNOWASM002" "$CUSTOM_LOG"
    rm -f "$CUSTOM_LOG"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit 1
fi

rm -f "$CUSTOM_LOG"

if [ "$CUSTOM_EXIT" -ne 0 ]; then
    echo -e "${RED}❌ FAIL: dotnet publish with custom PublishDir failed (exit $CUSTOM_EXIT)${NC}"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit "$CUSTOM_EXIT"
fi

echo -e "${GREEN}✓ Publish with custom PublishDir succeeded without errors${NC}"

# Find uno-config.js in the custom publish output
CUSTOM_CONFIG=$(find "$CUSTOM_PUBLISH_DIR/wwwroot" -name "uno-config.js" 2>/dev/null | head -1)

if [ ! -f "$CUSTOM_CONFIG" ]; then
    echo -e "${RED}❌ FAIL: uno-config.js not found in custom publish output${NC}"
    echo "Searched in: $CUSTOM_PUBLISH_DIR/wwwroot"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit 1
fi

echo "Found config at: $CUSTOM_CONFIG"

# Extract fingerprint
CUSTOM_FINGERPRINT=$(sed -n 's/.*dotnet_js_filename.*"dotnet\.\([a-z0-9]*\)\.js".*/\1/p' "$CUSTOM_CONFIG" | head -1)

if [ -z "$CUSTOM_FINGERPRINT" ]; then
    echo -e "${RED}❌ FAIL: No fingerprint found in custom publish uno-config.js${NC}"
    cat "$CUSTOM_CONFIG"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit 1
fi

echo -e "${GREEN}✓ Found fingerprint in custom publish config: $CUSTOM_FINGERPRINT${NC}"

# Verify dotnet.js exists (in _framework/ or publish root)
CUSTOM_DOTNET_JS="$CUSTOM_PUBLISH_DIR/wwwroot/_framework/dotnet.$CUSTOM_FINGERPRINT.js"
CUSTOM_DOTNET_JS_ROOT="$CUSTOM_PUBLISH_DIR/dotnet.js"
if [ -f "$CUSTOM_DOTNET_JS" ]; then
    echo -e "${GREEN}✓ Verified dotnet.$CUSTOM_FINGERPRINT.js exists (_framework layout)${NC}"
elif [ -f "$CUSTOM_DOTNET_JS_ROOT" ]; then
    echo -e "${GREEN}✓ Verified dotnet.js exists in publish root (hash-based fingerprint)${NC}"
else
    echo -e "${RED}❌ FAIL: dotnet.js not found in custom publish output${NC}"
    echo "Checked: $CUSTOM_DOTNET_JS"
    echo "Checked: $CUSTOM_DOTNET_JS_ROOT"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit 1
fi

# Verify no stale compressed files
if [ -f "$CUSTOM_CONFIG.gz" ]; then
    echo -e "${RED}❌ FAIL: Stale compressed file exists: $CUSTOM_CONFIG.gz${NC}"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit 1
fi
if [ -f "$CUSTOM_CONFIG.br" ]; then
    echo -e "${RED}❌ FAIL: Stale compressed file exists: $CUSTOM_CONFIG.br${NC}"
    rm -rf "$CUSTOM_PUBLISH_DIR"
    exit 1
fi

echo -e "${GREEN}✓ No stale compressed files in custom publish output${NC}"
rm -rf "$CUSTOM_PUBLISH_DIR"

# Test 9: Publish with custom absolute PublishDir and fingerprinting disabled
echo ""
echo "🚫 Test 9: Publish with custom PublishDir + WasmShellEnableDotnetJsFingerprinting=false"
echo "----------------------------------------"
CUSTOM_NOFP_DIR=$(mktemp -d)
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"

set +e
dotnet publish "$PROJECT_FILE" --configuration Release \
    -p:PublishDir="$CUSTOM_NOFP_DIR" \
    -p:WasmShellEnableDotnetJsFingerprinting=false 2>&1
CUSTOM_NOFP_EXIT=$?
set -e

if [ "$CUSTOM_NOFP_EXIT" -ne 0 ]; then
    echo -e "${RED}❌ FAIL: Publish with custom PublishDir + disabled fingerprinting failed${NC}"
    rm -rf "$CUSTOM_NOFP_DIR"
    exit "$CUSTOM_NOFP_EXIT"
fi

CUSTOM_NOFP_CONFIG=$(find "$CUSTOM_NOFP_DIR/wwwroot" -name "uno-config.js" 2>/dev/null | head -1)

if [ -f "$CUSTOM_NOFP_CONFIG" ]; then
    CUSTOM_NOFP_FP=$(sed -n 's/.*dotnet_js_filename.*"dotnet\.\([a-z0-9]*\)\.js".*/\1/p' "$CUSTOM_NOFP_CONFIG" | head -1)
    if [ -n "$CUSTOM_NOFP_FP" ]; then
        echo -e "${RED}❌ FAIL: Fingerprint found despite WasmShellEnableDotnetJsFingerprinting=false${NC}"
        echo "  Unexpected fingerprint: $CUSTOM_NOFP_FP"
        rm -rf "$CUSTOM_NOFP_DIR"
        exit 1
    fi
fi

echo -e "${GREEN}✓ Custom PublishDir + disabled fingerprinting: no fingerprint in config${NC}"
rm -rf "$CUSTOM_NOFP_DIR"

# Summary
echo ""
echo "========================================="
echo -e "${GREEN}✅ ALL TESTS PASSED${NC}"
echo "========================================="
echo ""
echo "Summary:"
echo "  Build fingerprint:          $BUILD_FINGERPRINT"
echo "  Publish fingerprint:        $PUBLISH_FINGERPRINT"
echo "  Custom PublishDir fp:       $CUSTOM_FINGERPRINT"
echo ""

if [ "$BUILD_FINGERPRINT" != "$PUBLISH_FINGERPRINT" ]; then
    echo -e "${YELLOW}ℹ Note: Fingerprints differ between build and publish (expected due to AOT/optimizations)${NC}"
fi

exit 0
