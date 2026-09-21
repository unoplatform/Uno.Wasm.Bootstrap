#!/bin/bash

# Validates the emscripten GL_UNPACK_ROW_LENGTH backport (emscripten-core/emscripten#21980)
# is applied to the runtime-generated _framework/dotnet.native.*.js when an app opts in to a
# 4GB heap via `-s MAXIMUM_MEMORY=4GB`, and is NOT applied otherwise.
#
# The test app references WebGL from a native C file (webgl-force-link.c) so emscripten links
# its WebGL JS glue (library_webgl.js) into dotnet.native.js — otherwise a minimal app would
# not contain the code the backport patches and the assertions would be vacuous.
set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$PROJECT_DIR/Uno.Wasm.Tests.WebGL4Gb.csproj"

# Marker text emitted by the backport, and the original (unpatched) emscripten token it replaces.
PATCH_MARKER='GL.unpackRowLength = param'
PATCH_BRANCH='GL_UNPACK_ROW_LENGTH'
PATCH_ROWSIZE='(GL.unpackRowLength || width)'
ORIGINAL_ROWSIZE='var plainRowSize = width * sizePerPixel;'

# Exact assignment the bootstrap must emit into uno-config.js when the 4GB flag is present, so the
# hosted app can read back the heap ceiling it was linked with (e.g. to size memory-pressure tiers).
MAXMEM_ENV_NAME='UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY'
MAXMEM_ENV_ASSIGNMENT='"UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY"] = "4GB"'

echo "========================================="
echo "Testing 4GB WebGL fix (emscripten #21980 backport)"
echo "========================================="

find_native_js() {
    # $1 = wwwroot root; echoes the single dotnet.native.*.js path (excluding compressed variants).
    find "$1/_framework" -name "dotnet.native.*.js" ! -name "*.js.br" ! -name "*.js.gz" 2>/dev/null | head -1
}

count() { grep -F -c -- "$2" "$1" 2>/dev/null || true; }

assert_patched() {
    # $1 = native js path
    local njs="$1"
    if [ ! -f "$njs" ]; then
        echo -e "${RED}❌ FAIL: dotnet.native.*.js not found${NC}"; exit 1
    fi
    echo "  Inspecting: $njs"
    if [ "$(count "$njs" "$PATCH_MARKER")" -lt 1 ]; then
        echo -e "${RED}❌ FAIL: fix marker '$PATCH_MARKER' not present — the backport did not apply.${NC}"
        echo "  (If the runtime now ships emscripten >= 3.1.61, the token text may have changed; update the backport.)"
        exit 1
    fi
    if [ "$(count "$njs" "$PATCH_BRANCH")" -lt 1 ]; then
        echo -e "${RED}❌ FAIL: '$PATCH_BRANCH' branch not present.${NC}"; exit 1
    fi
    if [ "$(count "$njs" "$PATCH_ROWSIZE")" -lt 1 ]; then
        echo -e "${RED}❌ FAIL: row-size expression was not rewritten to use unpackRowLength.${NC}"; exit 1
    fi
    if [ "$(count "$njs" "$ORIGINAL_ROWSIZE")" -ne 0 ]; then
        echo -e "${RED}❌ FAIL: original unpatched row-size token is still present.${NC}"; exit 1
    fi
    echo -e "${GREEN}✓ Fix applied (marker, branch, row-size rewrite present; original token replaced)${NC}"
}

find_uno_config() {
    # $1 = wwwroot root; echoes the app's uno-config.js (the one next to index.html, not a worker copy).
    find "$1" -maxdepth 1 -name 'uno-config.js' 2>/dev/null | head -1
}

assert_maxmem_env() {
    # $1 = wwwroot root. The bootstrap must emit UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY=4GB into
    # uno-config.js whenever the app links with -s MAXIMUM_MEMORY=4GB. Without it the heap ceiling
    # is invisible to managed code (this regressed once: the detection read an empty EmccFlags item).
    local cfg
    cfg="$(find_uno_config "$1")"
    if [ -z "$cfg" ] || [ ! -f "$cfg" ]; then
        echo -e "${RED}❌ FAIL: uno-config.js not found under $1${NC}"; exit 1
    fi
    echo "  Inspecting: $cfg"
    if [ "$(count "$cfg" "$MAXMEM_ENV_ASSIGNMENT")" -lt 1 ]; then
        echo -e "${RED}❌ FAIL: '$MAXMEM_ENV_NAME = \"4GB\"' not emitted into uno-config.js — the 4GB heap ceiling is invisible to the app.${NC}"
        exit 1
    fi
    echo -e "${GREEN}✓ uno-config.js exposes $MAXMEM_ENV_NAME=4GB${NC}"
}

assert_no_maxmem_env() {
    # $1 = wwwroot root. Without the 4GB flag the env var must NOT be emitted (gating check).
    local cfg
    cfg="$(find_uno_config "$1")"
    if [ -z "$cfg" ] || [ ! -f "$cfg" ]; then
        echo -e "${RED}❌ FAIL: uno-config.js not found under $1${NC}"; exit 1
    fi
    if [ "$(count "$cfg" "$MAXMEM_ENV_NAME")" -ne 0 ]; then
        echo -e "${RED}❌ FAIL: $MAXMEM_ENV_NAME emitted without the 4GB flag — gating is broken.${NC}"; exit 1
    fi
    echo -e "${GREEN}✓ uno-config.js does not expose $MAXMEM_ENV_NAME (no-flag build)${NC}"
}

assert_no_stale_compressed() {
    # $1 = native js path. The publish target deletes stale .br/.gz so a content-negotiating
    # server cannot serve the unpatched compressed bytes.
    local njs="$1"
    if [ -f "$njs.br" ]; then
        echo -e "${RED}❌ FAIL: stale compressed copy exists: $njs.br${NC}"; exit 1
    fi
    if [ -f "$njs.gz" ]; then
        echo -e "${RED}❌ FAIL: stale compressed copy exists: $njs.gz${NC}"; exit 1
    fi
    echo -e "${GREEN}✓ No stale .br/.gz copies of the patched dotnet.native.js${NC}"
}

# ---------------------------------------------------------------------------
echo ""
echo "📦 Test 1: Build with MAXIMUM_MEMORY=4GB (expect fix applied)"
echo "----------------------------------------"
dotnet clean "$PROJECT_FILE" -c Release > /dev/null 2>&1 || true
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"
dotnet build "$PROJECT_FILE" -c Release /m:1
BUILD_NJS="$(find_native_js "$PROJECT_DIR/bin/Release/net10.0/wwwroot")"
assert_patched "$BUILD_NJS"
assert_maxmem_env "$PROJECT_DIR/bin/Release/net10.0/wwwroot"

# ---------------------------------------------------------------------------
echo ""
echo "📤 Test 2: Publish with MAXIMUM_MEMORY=4GB (expect fix applied + no stale compressed copies)"
echo "----------------------------------------"
dotnet publish "$PROJECT_FILE" -c Release /m:1
PUBLISH_NJS="$(find_native_js "$PROJECT_DIR/bin/Release/net10.0/publish/wwwroot")"
assert_patched "$PUBLISH_NJS"
assert_no_stale_compressed "$PUBLISH_NJS"

# Note: we deliberately do NOT run `node --check` on the patched dotnet.native.js.
# The .NET runtime emits it as an ES module using modern syntax (import.meta,
# optional-chaining calls `?.()`), and the Node available on CI agents is often
# older than those features, producing false "invalid JavaScript" failures even
# though the file is valid. The marker assertions above (fix tokens present, the
# original row-size token replaced) already validate that the patch produced the
# intended, well-formed edit.

# ---------------------------------------------------------------------------
# The publish-output target resolves the publish directory through a fallback
# chain (PublishDir / -o / OutputPath / OutDir / conventional bin path), the same
# logic that has repeatedly broken for dotnet.js fingerprinting. The next three
# scenarios exercise the override paths a default `dotnet publish` does NOT.
echo ""
echo "📤 Test 3: Publish with -o output flag (override publish path)"
echo "----------------------------------------"
OUT_FLAG_DIR="$(mktemp -d)/publish-o"
dotnet publish "$PROJECT_FILE" -c Release /m:1 -o "$OUT_FLAG_DIR"
OUT_FLAG_NJS="$(find_native_js "$OUT_FLAG_DIR/wwwroot")"
assert_patched "$OUT_FLAG_NJS"
assert_no_stale_compressed "$OUT_FLAG_NJS"

echo ""
echo "📤 Test 4: Publish with explicit PublishDir property (override publish path)"
echo "----------------------------------------"
PUBLISHDIR_PROP="$(mktemp -d)/publish-dir-prop/"
dotnet publish "$PROJECT_FILE" -c Release /m:1 -p:PublishDir="$PUBLISHDIR_PROP"
PUBLISHDIR_NJS="$(find_native_js "${PUBLISHDIR_PROP}wwwroot")"
assert_patched "$PUBLISHDIR_NJS"
assert_no_stale_compressed "$PUBLISHDIR_NJS"

echo ""
echo "📤 Test 5: Build then publish as separate commands (fingerprint historically desynced here)"
echo "----------------------------------------"
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"
dotnet build "$PROJECT_FILE" -c Release /m:1
dotnet publish "$PROJECT_FILE" -c Release /m:1
BTP_NJS="$(find_native_js "$PROJECT_DIR/bin/Release/net10.0/publish/wwwroot")"
assert_patched "$BTP_NJS"
assert_no_stale_compressed "$BTP_NJS"

echo ""
echo "🔄 Test 6: Nested publish (WasmBuildingForNestedPublish=true) — targets must skip cleanly"
echo "----------------------------------------"
# The .NET WASM SDK's inner publish pass runs with WasmBuildingForNestedPublish=true and
# PublishDir pointing at an intermediate where the runtime glue is not finalized. Our targets
# must skip in that context (they key off this property), neither failing the build nor emitting
# the "could NOT be applied" stale-backport warning on the intermediate.
NESTED_LOG="$(mktemp)"
set +e
dotnet publish "$PROJECT_FILE" -c Release /m:1 -p:WasmBuildingForNestedPublish=true > "$NESTED_LOG" 2>&1
NESTED_EXIT=$?
set -e
if [ "$NESTED_EXIT" -ne 0 ]; then
    echo -e "${RED}❌ FAIL: nested publish failed (exit $NESTED_EXIT)${NC}"; cat "$NESTED_LOG"; rm -f "$NESTED_LOG"; exit 1
fi
if grep -q "could NOT be applied" "$NESTED_LOG"; then
    echo -e "${RED}❌ FAIL: WebGL targets ran during nested publish (emitted the stale-backport warning)${NC}"
    grep "could NOT be applied" "$NESTED_LOG"; rm -f "$NESTED_LOG"; exit 1
fi
rm -f "$NESTED_LOG"
echo -e "${GREEN}✓ Nested publish completed without running the WebGL targets${NC}"

# ---------------------------------------------------------------------------
echo ""
echo "🚫 Test 7: Build WITHOUT the 4GB flag (-p:Enable4Gb=false) — expect NO patch"
echo "----------------------------------------"
rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"
dotnet build "$PROJECT_FILE" -c Release /m:1 -p:Enable4Gb=false
NOFLAG_NJS="$(find_native_js "$PROJECT_DIR/bin/Release/net10.0/wwwroot")"
if [ ! -f "$NOFLAG_NJS" ]; then
    echo -e "${RED}❌ FAIL: dotnet.native.*.js not found in no-flag build${NC}"; exit 1
fi
if [ "$(count "$NOFLAG_NJS" "$PATCH_MARKER")" -ne 0 ]; then
    echo -e "${RED}❌ FAIL: fix marker present without the 4GB flag — gating is broken.${NC}"; exit 1
fi
if [ "$(count "$NOFLAG_NJS" "$ORIGINAL_ROWSIZE")" -lt 1 ]; then
    echo -e "${RED}❌ FAIL: expected the original (unpatched) emscripten token in the no-flag build, but it is absent.${NC}"
    echo "  (The WebGL glue should still be linked via webgl-force-link.c — this points at a setup change.)"
    exit 1
fi
assert_no_maxmem_env "$PROJECT_DIR/bin/Release/net10.0/wwwroot"
echo -e "${GREEN}✓ No-flag build is unpatched (gating works), and the WebGL glue is present${NC}"

echo ""
echo "========================================="
echo -e "${GREEN}✅ ALL TESTS PASSED${NC}"
echo "========================================="
exit 0
