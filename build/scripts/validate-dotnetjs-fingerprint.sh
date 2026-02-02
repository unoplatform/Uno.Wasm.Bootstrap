#!/bin/bash
# Validates that uno-config.js references a dotnet.js fingerprint that matches an actual file
set -e

PUBLISH_PATH=$1

if [ -z "$PUBLISH_PATH" ]; then
    echo "Usage: validate-dotnetjs-fingerprint.sh <publish-wwwroot-path>"
    exit 1
fi

echo "Validating dotnet.js fingerprint in: $PUBLISH_PATH"

# Find uno-config.js (it could be in a package_* subdirectory)
UNO_CONFIG=$(find "$PUBLISH_PATH" -name "uno-config.js" -type f | head -1)

if [ -z "$UNO_CONFIG" ]; then
    echo "ERROR: uno-config.js not found in $PUBLISH_PATH"
    exit 1
fi

echo "Found uno-config.js: $UNO_CONFIG"

# Extract the fingerprint from uno-config.js
FINGERPRINT=$(grep -oP 'dotnet\.\K[a-z0-9]+(?=\.js)' "$UNO_CONFIG" | head -1)

if [ -z "$FINGERPRINT" ]; then
    echo "ERROR: Could not extract dotnet.js fingerprint from uno-config.js"
    echo "Content of uno-config.js:"
    cat "$UNO_CONFIG"
    exit 1
fi

echo "Fingerprint in uno-config.js: $FINGERPRINT"

# Check if the fingerprinted dotnet.js file exists
FRAMEWORK_PATH="$PUBLISH_PATH/_framework"
DOTNET_JS_FILE="$FRAMEWORK_PATH/dotnet.$FINGERPRINT.js"

if [ ! -f "$DOTNET_JS_FILE" ]; then
    echo "ERROR: Fingerprint mismatch!"
    echo "  uno-config.js references: dotnet.$FINGERPRINT.js"
    echo "  But this file does not exist in: $FRAMEWORK_PATH"
    echo ""
    echo "Available dotnet.*.js files in _framework:"
    ls -la "$FRAMEWORK_PATH"/dotnet.*.js 2>/dev/null || echo "  (none found)"
    exit 1
fi

echo "SUCCESS: dotnet.$FINGERPRINT.js exists in _framework"
echo "Fingerprint validation passed!"
