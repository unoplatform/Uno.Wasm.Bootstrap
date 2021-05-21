#!/bin/bash
set -e

cleanup() {
	kill %%
}
trap cleanup 0

sudo npm install -g @azure/static-web-apps-cli

export BOOTSTRAP_APP_PATH=$1
export BOOTSTRAP_TEST_RUNNER_PATH=$2
export BOOTSTRAP_TEST_RUNNER_URL=$3

echo "BOOTSTRAP_APP_PATH=$BOOTSTRAP_APP_PATH"
echo "BOOTSTRAP_TEST_RUNNER_PATH=$BOOTSTRAP_TEST_RUNNER_PATH"
echo "BOOTSTRAP_TEST_RUNNER_URL=$BOOTSTRAP_TEST_RUNNER_URL"

cd $BOOTSTRAP_APP_PATH
swa start --port 8000 --app-location "$BOOTSTRAP_APP_PATH" &

cd $BOOTSTRAP_TEST_RUNNER_PATH
npm install
node app