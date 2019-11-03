#!/bin/bash
export EMSCRIPTEN_VERSION=1.38.48-upstream
export NUGET_PACKAGES=/workspace/.nuget

cd ~
git clone https://github.com/emscripten-core/emsdk.git
cd emsdk
echo Installing emscripten $EMSCRIPTEN_VERSION
./emsdk install $EMSCRIPTEN_VERSION
./emsdk activate $EMSCRIPTEN_VERSION

wget https://raw.githubusercontent.com/mono/mono/27247739c68faee7b2a63ae805222d4b375d2161/sdks/builds/emscripten-pr-8457.diff
wget https://raw.githubusercontent.com/mono/mono/27247739c68faee7b2a63ae805222d4b375d2161/sdks/builds/fix-emscripten-8511.diff

# apply patches
cd upstream/emscripten
patch -N -p1 < ~/emsdk/emscripten-pr-8457.diff
patch -N -p1 < ~/emsdk/fix-emscripten-8511.diff

cd /workspace/Uno.Wasm.Bootstrap

source ~/emsdk/emsdk_env.sh

msbuild /r /t:Publish /p:Configuration=Release /p:WasmShellMonoRuntimeExecutionMode=Interpreter src/Uno.Wasm.Bootstrap.sln