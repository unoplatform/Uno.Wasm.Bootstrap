#!/bin/bash
set -e

cleanup() {
	kill %%
}
trap cleanup 0

export BOOTSTRAP_APP_PATH=$1
export BOOTSTRAP_TEST_RUNNER_PATH=$2
export BOOTSTRAP_TEST_RUNNER_URL=$3

cd $BOOTSTRAP_APP_PATH
python $BUILD_SOURCESDIRECTORY/build/scripts/server.py &
cd $BOOTSTRAP_TEST_RUNNER_PATH
npm install
node app