#!/bin/bash

# Verifies AOT wrapper seeding on a profile-guided AOT app.
#
# Uno.Wasm.Tests.AOTWrapperSeeding.App calls interface methods from AOT-compiled code, and its aot.profile leaves out other methods of those
# interfaces, with more than 8 parameters, that hash to the same IMT slots. The script:
#   1. publishes the app with WasmShellEnableAOTWrapperSeeding=false, with no UNOWASM008, and checks that the runtime aborts (the app still
#      reproduces the problem);
#   2. publishes it with the seeding (the default) and checks that the build checked the wrappers, that the expected interp_in wrappers were
#      listed, those of same-named types of two assemblies included, that the build warned with UNOWASM010 about the method the runtime can't
#      enter and with no UNOWASM008, that the seed assembly isn't published, and that the app runs;
#   3. publishes it again, with a seed generation that fails, and checks that the build continues without the seeding, with the generation's
#      UNOWASM008: it doesn't generate the seeds, check the wrappers or AOT-compile the seed assembly, and AOT-compiles the deduplicated instances
#      again, without the seeds, although no other input of the AOT compiler changed;
#   4. publishes it without AOT deduplication and checks the seeding was skipped with the UNOWASM009 warning, and no UNOWASM008 (without
#      deduplication, the wrappers would only be in the seed assembly's AOT module, which the runtime doesn't load).
#
# Usage: ./test-aot-wrapper-seeding.sh [artifacts-dir]

set -e
set -o pipefail

SERVER_PID=""
cleanup() {
    if [ -n "$SERVER_PID" ]; then kill "$SERVER_PID" 2>/dev/null || true; fi
}
trap cleanup EXIT

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${BUILD_SOURCESDIRECTORY:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
ARTIFACTS_DIR="${1:-$SCRIPT_DIR/artifacts}"
mkdir -p "$ARTIFACTS_DIR"

# MSBuild gets the artifact paths inside its own arguments (LogFile=...), which Git Bash on Windows doesn't convert: C:/... works in both.
if command -v cygpath > /dev/null; then
    ARTIFACTS_DIR="$(cygpath -m "$ARTIFACTS_DIR")"
fi

APP_DIR="$REPO_ROOT/src/Uno.Wasm.Tests.AOTWrapperSeeding.App"
TFM="net10.0"
UNSEEDED_DIR="$APP_DIR/bin/Release/$TFM/publish-unseeded"
SEEDED_DIR="$APP_DIR/bin/Release/$TFM/publish-seeded"
FAILURE_DIR="$APP_DIR/bin/Release/$TFM/publish-generation-failure"
NODEDUP_DIR="$APP_DIR/bin/Release/$TFM/publish-nodedup"
SEEDING_DIR="$APP_DIR/obj/Release/$TFM/aot-wrapper-seeding"
EXPECTED_WRAPPERS="$SEEDING_DIR/Uno.Wasm.AotWrapperSeeds.wrappers.txt"
SEEDED_LOG="$ARTIFACTS_DIR/AOTWrapperSeeding-seeded.log"

# The interp_in wrapper of IService.GetRecords (string, int, int, bool, long, long, double, double, int)
GETRECORDS_WRAPPER="wrapper_other_object_interp_in_generic_intptr_intptr_intptr_intptr_long_long_double_double_intptr"

# The interp_in wrapper of IMerger<string>.Aggregate (string, double, string, double, string, double, string, double, string)
AGGREGATE_WRAPPER="wrapper_other_object_interp_in_generic_intptr_double_intptr_double_intptr_double_intptr_double_intptr"

# The interp_in wrappers of Take (Value, int, int, int, int, int, int, int, int) in TwinA and TwinB, each with its own Twins.Value: two wrappers
# of the same name
TAKE_WRAPPER="wrapper_other_object_interp_in_generic_Uno_Wasm_Tests_AOTWrapperSeeding_Twins_Value_intptr_intptr_intptr_intptr_intptr_intptr_intptr_intptr"

# The interp_in wrappers of the methods of the Twins.Service classes of TwinA and TwinB
BLEND_WRAPPER="wrapper_other_object_interp_in_generic_long_double_long_double_long_double_long_double_long"
MIX_WRAPPER="wrapper_other_object_interp_in_generic_double_long_double_long_double_long_double_long_double"

# Whether the expected wrappers list the wrapper (the name, then a tab and a method that needs it), once or the given number of times
expects_wrapper() {
    local count
    count="$(grep -c "^${1}"$'\t' "$EXPECTED_WRAPPERS" || true)"
    [ "$count" = "${2:-1}" ]
}

echo "========================================="
echo "AOT Wrapper Seeding Test"
echo "========================================="

# Each publish starts from an empty obj folder: the AOT compiler reuses the bitcode it finds there, which `dotnet clean` keeps.
echo "Step 1: Publishing without AOT wrapper seeding..."
cd "$APP_DIR"
rm -rf "$APP_DIR/obj"
UNSEEDED_LOG="$ARTIFACTS_DIR/AOTWrapperSeeding-unseeded.log"
dotnet publish -c Release -m:1 -p:WasmShellEnableAOTWrapperSeeding=false -o "$UNSEEDED_DIR" \
    -bl:"$ARTIFACTS_DIR/AOTWrapperSeeding-unseeded.binlog" -flp:"LogFile=$UNSEEDED_LOG;Verbosity=normal"

if grep -q "UNOWASM008" "$UNSEEDED_LOG"; then
    echo -e "${RED}FAIL: the build without the seeding warned with UNOWASM008.${NC}"
    exit 1
fi
echo -e "${GREEN}  The build without the seeding didn't warn with UNOWASM008.${NC}"

echo "Step 2: Publishing with AOT wrapper seeding..."
rm -rf "$APP_DIR/obj"
dotnet publish -c Release -m:1 -o "$SEEDED_DIR" \
    -bl:"$ARTIFACTS_DIR/AOTWrapperSeeding-seeded.binlog" -flp:"LogFile=$SEEDED_LOG;Verbosity=normal"

if ! grep -q "\[Uno\] AOT wrapper seeding check:" "$SEEDED_LOG"; then
    echo -e "${RED}FAIL: the build didn't check that the AOT compilation emitted the expected wrappers.${NC}"
    exit 1
fi
echo -e "${GREEN}  The build checked that the AOT compilation emitted the expected wrappers.${NC}"

if ! expects_wrapper "$GETRECORDS_WRAPPER"; then
    echo -e "${RED}FAIL: $EXPECTED_WRAPPERS doesn't list the interp_in wrapper of IService.GetRecords.${NC}"
    exit 1
fi
echo -e "${GREEN}  The interp_in wrapper of IService.GetRecords is expected (and the build checked it was emitted).${NC}"

if ! expects_wrapper "$AGGREGATE_WRAPPER"; then
    echo -e "${RED}FAIL: $EXPECTED_WRAPPERS doesn't list the interp_in wrapper of IMerger<string>.Aggregate.${NC}"
    exit 1
fi
echo -e "${GREEN}  The interp_in wrapper of IMerger<string>.Aggregate is expected (and the build checked it was emitted).${NC}"

if ! expects_wrapper "$TAKE_WRAPPER" 2; then
    echo -e "${RED}FAIL: $EXPECTED_WRAPPERS doesn't list the interp_in wrappers of both Take methods of TwinA and TwinB.${NC}"
    exit 1
fi
echo -e "${GREEN}  The interp_in wrappers of both Take methods of TwinA and TwinB, whose names are the same, are expected.${NC}"

if ! expects_wrapper "$BLEND_WRAPPER" || ! expects_wrapper "$MIX_WRAPPER"; then
    echo -e "${RED}FAIL: $EXPECTED_WRAPPERS doesn't list the interp_in wrappers of both Twins.Service classes.${NC}"
    exit 1
fi
echo -e "${GREEN}  The interp_in wrappers of same-named types of TwinA and TwinB are expected (and the build checked they were emitted).${NC}"

# The trimmed assembly has no parameter names, so the warning gives the parameter's position.
if ! grep -q "UNOWASM010.*AttributedParameters::Describe.*parameter 9" "$SEEDED_LOG"; then
    echo -e "${RED}FAIL: the build didn't warn with UNOWASM010 about parameter 9 of AttributedParameters.Describe.${NC}"
    exit 1
fi
echo -e "${GREEN}  The build warned with UNOWASM010 about parameter 9 of AttributedParameters.Describe.${NC}"

if grep -q "UNOWASM008" "$SEEDED_LOG"; then
    echo -e "${RED}FAIL: the build warned with UNOWASM008.${NC}"
    exit 1
fi
echo -e "${GREEN}  The build didn't warn with UNOWASM008.${NC}"

if [ -n "$(find "$SEEDED_DIR" -name "Uno.Wasm.AotWrapperSeeds*")" ]; then
    echo -e "${RED}FAIL: the seed assembly was published.${NC}"
    exit 1
fi
echo -e "${GREEN}  The seed assembly isn't published.${NC}"

# The generation fails when it can't write the expected wrappers, here because a folder has their name. It deletes the files of the earlier
# publish, which the build must then not use. Touching the trimmed app assembly makes it newer than the generation's stamp, so that the
# generation runs, without changing the content of any input of the AOT compiler (the trimming may also run again and write the same file),
# which would compile everything again by itself: only the bootstrapper's targets make it compile the deduplicated instances again without the
# seed assembly, which it would otherwise consider up to date.
echo "Step 3: Publishing again, with a seed generation that fails..."
FAILURE_LOG="$ARTIFACTS_DIR/AOTWrapperSeeding-generation-failure.log"
rm "$EXPECTED_WRAPPERS"
mkdir "$EXPECTED_WRAPPERS"
touch "$APP_DIR/obj/Release/$TFM/linked/Uno.Wasm.Tests.AOTWrapperSeeding.App.dll"

if ! dotnet publish -c Release -m:1 -o "$FAILURE_DIR" \
    -bl:"$ARTIFACTS_DIR/AOTWrapperSeeding-generation-failure.binlog" -flp:"LogFile=$FAILURE_LOG;Verbosity=normal"; then
    echo -e "${RED}FAIL: the build failed after the seed generation failed.${NC}"
    exit 1
fi

# The generation's UNOWASM008 gives the exception, which names the expected wrappers file.
if ! grep -q "UNOWASM008.*Uno.Wasm.AotWrapperSeeds.wrappers.txt" "$FAILURE_LOG" \
    || grep -q "\[Uno\] AOT wrapper seeding:" "$FAILURE_LOG" \
    || grep -q "\[Uno\] AOT wrapper seeding check:" "$FAILURE_LOG"; then
    echo -e "${RED}FAIL: the build didn't continue without the seeding, with UNOWASM008, when the seed generation failed.${NC}"
    exit 1
fi

# The AOT compiler logs each assembly whose output changes: "[1/7] aot-instances.dll -> aot-instances.dll.bc". Without the seeds, the deduplicated
# instances change; with nothing to compile, it logs "Everything is up-to-date, nothing to precompile".
if grep -q "Uno.Wasm.AotWrapperSeeds.dll -> " "$FAILURE_LOG" || ! grep -q "aot-instances.dll -> " "$FAILURE_LOG"; then
    echo -e "${RED}FAIL: the AOT compilation didn't compile the app again without the seed assembly.${NC}"
    exit 1
fi
rmdir "$EXPECTED_WRAPPERS"
echo -e "${GREEN}  The build continued without the seeding, with UNOWASM008, when the seed generation failed, and compiled the app without it.${NC}"

echo "Step 4: Publishing without AOT deduplication..."
rm -rf "$APP_DIR/obj"
dotnet publish -c Release -m:1 -p:WasmShellEnableAOTDeduplication=false -o "$NODEDUP_DIR" \
    -bl:"$ARTIFACTS_DIR/AOTWrapperSeeding-nodedup.binlog" | tee "$ARTIFACTS_DIR/AOTWrapperSeeding-nodedup.log"

if ! grep -q "UNOWASM009" "$ARTIFACTS_DIR/AOTWrapperSeeding-nodedup.log" || grep -q "UNOWASM008" "$ARTIFACTS_DIR/AOTWrapperSeeding-nodedup.log" \
    || [ -e "$SEEDING_DIR/Uno.Wasm.AotWrapperSeeds.dll" ]; then
    echo -e "${RED}FAIL: the build without AOT deduplication didn't skip the seeding with UNOWASM009.${NC}"
    exit 1
fi
echo -e "${GREEN}  The seeding was skipped with UNOWASM009.${NC}"

echo "Step 5: Runtime validation..."
# Run by its path, dotnet-serve is the process that serves, which its PID stops ("dotnet serve" would start it as a child).
dotnet tool install dotnet-serve --version 1.10.175 --tool-path "$REPO_ROOT/build/tools" 2>/dev/null || true
DOTNET_SERVE="$REPO_ROOT/build/tools/dotnet-serve"

cd "$SCRIPT_DIR"
npm ci --no-audit --no-fund

# A port nothing listens on
free_port() {
    python3 -c 'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()' 2>/dev/null \
        || python -c 'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()'
}

run_app() {
    local site="$1"
    local expectation="$2"
    local port
    port="$(free_port)"

    "$DOTNET_SERVE" -p "$port" -c -b -d "$site/wwwroot" &
    SERVER_PID=$!

    for _ in $(seq 1 60); do
        if curl -s -o /dev/null "http://localhost:$port/"; then break; fi
        sleep 1
    done

    local result=0
    node validate-aot-wrapper-seeding.js "$expectation" "$ARTIFACTS_DIR" "http://localhost:$port/" || result=$?

    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
    SERVER_PID=""
    return $result
}

# Fails with the given message when the app doesn't end as expected, and with its own when the browser fails to launch (validator exit code 3)
check_app() {
    local result=0
    run_app "$1" "$2" || result=$?

    if [ "$result" -eq 3 ]; then
        echo -e "${RED}FAIL: the browser failed to launch (the error is above), so the app wasn't checked.${NC}"
        exit 1
    fi

    if [ "$result" -ne 0 ]; then
        echo -e "${RED}FAIL: $3${NC}"
        exit 1
    fi
}

check_app "$UNSEEDED_DIR" abort "the build without AOT wrapper seeding didn't abort as expected."
check_app "$SEEDED_DIR" pass "the build with AOT wrapper seeding didn't run."

echo "========================================="
echo -e "${GREEN}ALL TESTS PASSED${NC}"
echo "========================================="
