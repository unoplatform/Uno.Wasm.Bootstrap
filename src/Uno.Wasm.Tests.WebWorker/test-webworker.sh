#!/bin/bash

# Test script to verify WebWorker shell mode works correctly.
# Publishes the WebWorker.Host app (which in turn publishes the WebWorker.App
# and copies its output into _worker/), validates the generated config and
# worker.js, then launches headless Chrome to confirm the host+worker
# integration works end-to-end.
#
# Usage: ./test-webworker.sh [artifacts-dir]

set -e

SERVER_PID=""
cleanup() {
    if [ -n "$SERVER_PID" ]; then
        kill "$SERVER_PID" 2>/dev/null || true
    fi
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

HOST_DIR="$REPO_ROOT/src/Uno.Wasm.Tests.WebWorker.Host"
PUBLISH_DIR="$HOST_DIR/bin/Release/net10.0/publish"
WWWROOT="$PUBLISH_DIR/wwwroot"

echo "========================================="
echo "WebWorker Mode Test (Host + Worker)"
echo "========================================="
echo ""

# -------------------------------------------------------------------
# Step 1: Publish the host app (triggers worker publish via MSBuild target)
# -------------------------------------------------------------------
echo "Step 1: Publishing WebWorker host app..."
echo "-----------------------------------------"
cd "$HOST_DIR"
dotnet clean -c Release > /dev/null 2>&1 || true
dotnet publish -c Release /m:1 \
    /bl:"$ARTIFACTS_DIR/WebWorkerHost-linux.binlog"

if [ ! -d "$WWWROOT" ]; then
    echo -e "${RED}FAIL: Published output not found at $WWWROOT${NC}"
    exit 1
fi

echo -e "${GREEN}Publish succeeded.${NC}"
echo ""

# -------------------------------------------------------------------
# Step 2: Validate host's uno-config.js (Browser mode)
# -------------------------------------------------------------------
echo "Step 2: Validating host uno-config.js..."
echo "-----------------------------------------"

HOST_CONFIG_JS=$(find "$WWWROOT" -maxdepth 2 -name "uno-config.js" -not -path "*/_worker/*" 2>/dev/null | head -1)
if [ ! -f "$HOST_CONFIG_JS" ]; then
    echo -e "${RED}FAIL: Host uno-config.js not found in $WWWROOT${NC}"
    exit 1
fi

if ! grep -q 'uno_shell_mode = "Browser"' "$HOST_CONFIG_JS"; then
    echo -e "${RED}FAIL: Host uno_shell_mode not set to Browser${NC}"
    grep 'uno_shell_mode' "$HOST_CONFIG_JS" || echo "  (property not found)"
    exit 1
fi
echo -e "${GREEN}  Host: uno_shell_mode = \"Browser\"${NC}"

echo ""

# -------------------------------------------------------------------
# Step 3: Validate worker files under _worker/
# -------------------------------------------------------------------
echo "Step 3: Validating _worker/ directory..."
echo "-----------------------------------------"

WORKER_DIR="$WWWROOT/_worker"
if [ ! -d "$WORKER_DIR" ]; then
    echo -e "${RED}FAIL: _worker/ directory not found in $WWWROOT${NC}"
    exit 1
fi
echo -e "${GREEN}  _worker/ directory exists${NC}"

WORKER_JS="$WORKER_DIR/worker.js"
if [ ! -f "$WORKER_JS" ]; then
    echo -e "${RED}FAIL: worker.js not found in _worker/${NC}"
    exit 1
fi
echo -e "${GREEN}  _worker/worker.js found${NC}"

if [ ! -d "$WORKER_DIR/_framework" ]; then
    echo -e "${RED}FAIL: _framework/ not found in _worker/${NC}"
    exit 1
fi
echo -e "${GREEN}  _worker/_framework/ found (separate runtime)${NC}"

# Validate worker's config
WORKER_CONFIG_JS=$(find "$WORKER_DIR" -maxdepth 2 -name "uno-config.js" 2>/dev/null | head -1)
if [ -f "$WORKER_CONFIG_JS" ]; then
    if grep -q 'uno_shell_mode = "WebWorker"' "$WORKER_CONFIG_JS"; then
        echo -e "${GREEN}  Worker: uno_shell_mode = \"WebWorker\"${NC}"
    fi
fi

echo ""

# -------------------------------------------------------------------
# Step 3b: Regression check — no orphan StaticWebAssetEndpoint routes.
#
# The host integration MUST NOT import the worker manifest's endpoints
# directly: their Route metadata points to the worker's original
# (root-relative) paths and would not include WasmShellWorkerBasePath.
# The host pipeline (DefineStaticWebAssetEndpoints) re-derives them from
# the re-registered StaticWebAsset items. Restoring the import would
# leave orphan endpoints whose Route is a "naked" worker filename.
#
# Asserts there is no endpoint whose Route equals 'worker.js' or starts
# with 'package_<workerHash>/' without the '_worker/' prefix in
# staticwebassets.publish.endpoints.json.
# -------------------------------------------------------------------
echo "Step 3b: Regression check — endpoint manifest has no orphan worker routes..."
echo "-----------------------------------------"

ENDPOINTS_JSON=$(find "$HOST_DIR/obj/Release" -name "staticwebassets.publish.endpoints.json" 2>/dev/null | head -1)
if [ ! -f "$ENDPOINTS_JSON" ]; then
    # Fallback: build-time manifest (covers `dotnet build` runs without publish).
    ENDPOINTS_JSON=$(find "$HOST_DIR/obj/Release" -name "staticwebassets.build.endpoints.json" 2>/dev/null | head -1)
fi

if [ ! -f "$ENDPOINTS_JSON" ]; then
    echo -e "${RED}FAIL: endpoint manifest not found in $HOST_DIR/obj/Release${NC}"
    exit 1
fi
echo "  Manifest: $ENDPOINTS_JSON"

# Determine worker package hash from the published worker config so we can
# detect orphans whose Route starts with the worker's package folder but
# without the _worker/ prefix.
WORKER_PACKAGE_HASH=""
if [ -f "$WORKER_CONFIG_JS" ]; then
    WORKER_PACKAGE_PATH=$(dirname "$WORKER_CONFIG_JS")
    WORKER_PACKAGE_HASH=$(basename "$WORKER_PACKAGE_PATH")
fi

ORPHANS=$(python3 - "$ENDPOINTS_JSON" "$WORKER_PACKAGE_HASH" <<'PY'
import json, sys
path = sys.argv[1]
worker_pkg = sys.argv[2]
with open(path) as f:
    data = json.load(f)
orphans = []
for ep in data.get("Endpoints", []):
    route = ep.get("Route", "")
    # Orphan = bare 'worker.js' (without _worker/ prefix), or starts with
    # the worker's own package_<hash>/ but not under _worker/.
    if route == "worker.js":
        orphans.append(route)
    elif worker_pkg and route.startswith(worker_pkg + "/"):
        orphans.append(route)
if orphans:
    for r in orphans[:5]:
        print(r)
    sys.exit(1)
PY
) || ORPHAN_FOUND=1

if [ "${ORPHAN_FOUND:-0}" = "1" ]; then
    echo -e "${RED}FAIL: orphan worker-mapped endpoints found in $ENDPOINTS_JSON:${NC}"
    echo "$ORPHANS" | sed 's/^/  /'
    echo
    echo "  This indicates _UnoBuildAndImportWebWorkerAssets is re-importing"
    echo "  the worker's manifest endpoints (StaticWebAssetEndpoint Include) —"
    echo "  drop that import; the host pipeline derives correct endpoints from"
    echo "  the re-registered StaticWebAsset items."
    exit 1
fi
echo -e "${GREEN}  No orphan worker-mapped endpoints detected${NC}"

echo ""

# -------------------------------------------------------------------
# Step 3c: Regression check — uno_dependencies URL construction
# -------------------------------------------------------------------
echo "Step 3c: Regression check — worker dependency URL resolution..."
echo "-----------------------------------------"

cd "$SCRIPT_DIR"
node validate-deps-url.js
echo ""

# -------------------------------------------------------------------
# Step 4: Serve and run Puppeteer validation
# -------------------------------------------------------------------
echo "Step 4: Runtime validation..."
echo "-----------------------------------------"

# Install dotnet-serve
dotnet tool uninstall dotnet-serve -g 2>/dev/null || true
dotnet tool uninstall dotnet-serve --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
dotnet tool install dotnet-serve --version 1.10.175 --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
export PATH="$PATH:$REPO_ROOT/build/tools"

# Start the server with COOP/COEP headers
echo "Starting HTTP server..."
cd "$WWWROOT"
dotnet serve -p 8001 -c -b \
    -h "Cross-Origin-Embedder-Policy: require-corp" \
    -h "Cross-Origin-Opener-Policy: same-origin" &
SERVER_PID=$!
sleep 5

# Install Puppeteer dependencies (prefer npm ci for lockfile-exact installs;
# fall back to npm install when the CI agent's npm is too old for lockfileVersion 3)
cd "$SCRIPT_DIR"
npm ci 2>/dev/null || npm install

# Run the Puppeteer-based validation
echo ""
echo "Running runtime validation..."
RESULT=0
node validate-webworker.js "$ARTIFACTS_DIR" "http://localhost:8001/" || RESULT=$?

echo ""
echo "-----------------------------------------"

if [ "$RESULT" -ne 0 ]; then
    echo -e "${RED}FAIL: WebWorker runtime validation failed.${NC}"
    exit 1
fi

echo ""
echo "========================================="
echo -e "${GREEN}ALL TESTS PASSED${NC}"
echo "========================================="

exit 0
