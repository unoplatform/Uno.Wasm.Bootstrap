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
