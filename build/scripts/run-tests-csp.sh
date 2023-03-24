#!/bin/bash
set -e

cleanup() {
	kill %%
}
trap cleanup 0

export BOOTSTRAP_APP_PATH=$1
export BOOTSTRAP_TEST_RUNNER_PATH=$2
export BOOTSTRAP_TEST_RUNNER_URL=$3

# install dotnet serve / Remove as needed
dotnet tool uninstall dotnet-serve -g || true
dotnet tool uninstall dotnet-serve --tool-path $BUILD_SOURCESDIRECTORY/build/tools || true
dotnet tool install dotnet-serve --version 1.10.140 --tool-path $BUILD_SOURCESDIRECTORY/build/tools || true
export PATH="$PATH:$BUILD_SOURCESDIRECTORY/build/tools"

cd $BOOTSTRAP_APP_PATH
dotnet serve -p 8000 -c -b \
	-h "Cross-Origin-Embedder-Policy: require-corp" \
	-h "Cross-Origin-Opener-Policy: same-origin" \
	-h "Content-Security-Policy: default-src 'self'; script-src 'self' 'wasm-unsafe-eval'" \
	&
sleep 5
cd $BOOTSTRAP_TEST_RUNNER_PATH
npm install
node app