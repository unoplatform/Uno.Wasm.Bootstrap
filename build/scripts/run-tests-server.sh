#!/bin/bash
set -e

cleanup() {
	kill %%
}
trap cleanup 0

npm install @azure/static-web-apps-cli@0.8.3
SWA_PATH=`pwd`/node_modules/.bin/swa

export BOOTSTRAP_APP_PATH=$1
export BOOTSTRAP_TEST_RUNNER_PATH=$2
export BOOTSTRAP_TEST_RUNNER_URL=$3

echo "BOOTSTRAP_APP_PATH=$BOOTSTRAP_APP_PATH"
echo "BOOTSTRAP_TEST_RUNNER_PATH=$BOOTSTRAP_TEST_RUNNER_PATH"
echo "BOOTSTRAP_TEST_RUNNER_URL=$BOOTSTRAP_TEST_RUNNER_URL"

cd $BOOTSTRAP_APP_PATH
dotnet build -c Release

# We're not running using the published build, so we need to set
# environment first.
export ASPNETCORE_ENVIRONMENT=development
dotnet run -c Release --no-build --urls=http://localhost:8000/ &
sleep 5

cd $BOOTSTRAP_TEST_RUNNER_PATH
npm install
node app