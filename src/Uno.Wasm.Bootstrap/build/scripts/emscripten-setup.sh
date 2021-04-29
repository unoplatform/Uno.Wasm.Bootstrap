#!/bin/bash
set -e

UNO_EMSDK_VERSION=$2
UNO_INTERMEDIATE_PATH=$1

set

echo "UNO_INTERMEDIATE_PATH: $UNO_INTERMEDIATE_PATH"
echo "UNO_EMSDK_VERSION: $UNO_EMSDK_VERSION"

echo Validating Mono Version
mono --version

echo Validating MSBuild Version
msbuild /version

export UNO_EMSDK_PATH="$UNO_INTERMEDIATE_PATH/emsdk-$UNO_EMSDK_VERSION"
export UNO_EMSDK_PATH_MARKER="$UNO_EMSDK_PATH/.uno-install-done"

echo "UNO_EMSDK_PATH: $UNO_EMSDK_PATH"
echo "UNO_EMSDK_PATH_MARKER: $UNO_EMSDK_PATH_MARKER"

if [ ! -f "$UNO_EMSDK_PATH" ]; then
	mkdir -p "$UNO_EMSDK_PATH"
fi

pushd "$UNO_EMSDK_PATH"

if [ ! -f "$UNO_EMSDK_PATH_MARKER" ]; then

	echo "Installing emscripten $UNO_EMSDK_VERSION in $UNO_EMSDK_PATH"

	git clone --branch $UNO_EMSDK_VERSION https://github.com/emscripten-core/emsdk 2>&1
	cd emsdk
	./emsdk install $UNO_EMSDK_VERSION 2>&1
	./emsdk activate $UNO_EMSDK_VERSION 2>&1

    echo "Writing $UNO_EMSDK_PATH_MARKER"
	touch "$UNO_EMSDK_PATH_MARKER"
else
	echo "Skipping installed emscripten $UNO_EMSDK_VERSION in $UNO_EMSDK_PATH"
fi
