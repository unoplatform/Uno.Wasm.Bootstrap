#!/bin/bash
export EMSCRIPTEN_VERSION=1.39.11
export NUGET_PACKAGES=/workspace/.nuget

cd /workspace/Uno.Wasm.Bootstrap

source ~/emsdk/emsdk_env.sh

msbuild /r /t:Publish /p:Configuration=Release /p:WasmShellMonoRuntimeExecutionMode=Interpreter src/Uno.Wasm.Bootstrap.sln