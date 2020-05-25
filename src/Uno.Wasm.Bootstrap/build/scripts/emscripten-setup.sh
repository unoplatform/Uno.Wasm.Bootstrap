#!/bin/bash
set -e

UNO_EMSDK_VERSION=$2
UNO_INTERMEDIATE_PATH=$1

echo "UNO_INTERMEDIATE_PATH: $UNO_INTERMEDIATE_PATH"
echo "UNO_EMSDK_VERSION: $UNO_EMSDK_VERSION"

echo Validating Mono Version
mono --version

echo Validating MSBuild Version
msbuild /version

export UNO_EMSDK_PATH=`wslpath "$UNO_INTERMEDIATE_PATH"`/emsdk-$UNO_EMSDK_VERSION

echo "UNO_EMSDK_PATH: $UNO_EMSDK_PATH"

if [ ! -f $UNO_EMSDK_PATH ]; then
	mkdir -p $UNO_EMSDK_PATH
fi

pushd $UNO_EMSDK_PATH

if [ ! -f .uno-install-done ]; then

	echo "Installing emscripten $UNO_EMSDK_VERSION in $UNO_EMSDK_PATH"

	git clone https://github.com/emscripten-core/emsdk 2>&1
	cd emsdk
	./emsdk install $UNO_EMSDK_VERSION
	./emsdk activate --embedded $UNO_EMSDK_VERSION

	# Those two files need to follow the currently used build of mono
	wget https://raw.githubusercontent.com/mono/mono/b777471fcace85325e2c2af0e460f4ecd8059b5a/sdks/builds/fix-emscripten-8511.diff 2>&1

	pushd upstream/emscripten
	patch -N -p1 < ../../fix-emscripten-8511.diff
	popd

	touch $UNO_EMSDK_PATH/.uno-install-done
else
	echo "Skipping installed emscripten $UNO_EMSDK_VERSION in $UNO_EMSDK_PATH"
fi
