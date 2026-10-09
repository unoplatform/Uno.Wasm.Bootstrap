#!/bin/bash
# usage: run-test.sh <published wwwroot of the RayTracer sample>
set -e

cd "$(dirname "${BASH_SOURCE[0]}")"
npm install --no-audit --no-fund

node server.js "$1" 8002 &
SERVER_PID=$!
trap 'kill $SERVER_PID' EXIT
sleep 2

node app.js "http://localhost:8002/"
