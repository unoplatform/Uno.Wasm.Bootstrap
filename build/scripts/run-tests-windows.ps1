#!/bin/bash
set -e

cleanup() {
	kill %%
}
trap cleanup 0

dotnet tool install dotnet-serve --version 1.8.15 --tool-path $BUILD_SOURCESDIRECTORY\build\tools
PATH="$PATH:$BUILD_SOURCESDIRECTORY\build\tools"

BOOTSTRAP_APP_PATH=$1
BOOTSTRAP_TEST_RUNNER_PATH=$2

cd $BOOTSTRAP_APP_PATH
$scriptBlock = {
dotnet serve -p 8000
}

Start-Job -Name webserver -ScriptBlock $scriptBlock

cd $BOOTSTRAP_TEST_RUNNER_PATH
npm install
node app

Stop-Job webserver