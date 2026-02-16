#!/bin/bash
# Validates that dotnet.js contains parseable embedded boot config with json-start/json-end markers.
set -e

PUBLISH_PATH=$1

if [ -z "$PUBLISH_PATH" ]; then
    echo "Usage: validate-boot-config.sh <publish-wwwroot-path>"
    exit 1
fi

echo "Validating embedded boot config in: $PUBLISH_PATH"

# Find uno-config.js (it could be in a package_* subdirectory)
UNO_CONFIG=$(find "$PUBLISH_PATH" -name "uno-config.js" -type f | head -1)

if [ -z "$UNO_CONFIG" ]; then
    echo "ERROR: uno-config.js not found in $PUBLISH_PATH"
    exit 1
fi

echo "Found uno-config.js: $UNO_CONFIG"

# Extract dotnet_js_filename from uno-config.js
DOTNET_JS_FILENAME=$(sed -n 's/.*dotnet_js_filename\s*=\s*"\([^"]*\)".*/\1/p' "$UNO_CONFIG" | head -1)

if [ -z "$DOTNET_JS_FILENAME" ]; then
    echo "ERROR: Could not extract dotnet_js_filename from uno-config.js"
    echo "Content of uno-config.js:"
    cat "$UNO_CONFIG"
    exit 1
fi

echo "dotnet_js_filename: $DOTNET_JS_FILENAME"

# Read the corresponding dotnet.js file
FRAMEWORK_PATH="$PUBLISH_PATH/_framework"
DOTNET_JS_FILE="$FRAMEWORK_PATH/$DOTNET_JS_FILENAME"

if [ ! -f "$DOTNET_JS_FILE" ]; then
    echo "ERROR: $DOTNET_JS_FILENAME not found in $FRAMEWORK_PATH"
    exit 1
fi

echo "Found dotnet.js: $DOTNET_JS_FILE"

# Validate json-start and json-end markers are present
if ! grep -q '/\*json-start\*/' "$DOTNET_JS_FILE"; then
    echo "ERROR: /*json-start*/ marker not found in $DOTNET_JS_FILENAME"
    echo "The embedded boot config format may have changed. The version checker depends on these markers."
    exit 1
fi

if ! grep -q '/\*json-end\*/' "$DOTNET_JS_FILE"; then
    echo "ERROR: /*json-end*/ marker not found in $DOTNET_JS_FILENAME"
    echo "The embedded boot config format may have changed. The version checker depends on these markers."
    exit 1
fi

# Extract the JSON between markers and validate it contains required fields
BOOT_JSON=$(sed -n 's|.*\/\*json-start\*\/\(.*\)\/\*json-end\*\/.*|\1|p' "$DOTNET_JS_FILE" | head -1)

if [ -z "$BOOT_JSON" ]; then
    echo "ERROR: Could not extract JSON between markers"
    exit 1
fi

# Validate mainAssemblyName is present
if ! echo "$BOOT_JSON" | grep -q '"mainAssemblyName"'; then
    echo "ERROR: mainAssemblyName not found in embedded boot config JSON"
    echo "Extracted JSON: $BOOT_JSON"
    exit 1
fi

# Validate resources is present
if ! echo "$BOOT_JSON" | grep -q '"resources"'; then
    echo "ERROR: resources not found in embedded boot config JSON"
    echo "Extracted JSON: $BOOT_JSON"
    exit 1
fi

echo "SUCCESS: Embedded boot config is valid"
echo "  - /*json-start*/ and /*json-end*/ markers present"
echo "  - mainAssemblyName field present"
echo "  - resources field present"
echo "Boot config validation passed!"
