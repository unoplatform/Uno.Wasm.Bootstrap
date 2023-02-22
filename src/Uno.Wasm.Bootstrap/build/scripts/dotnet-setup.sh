#!/bin/bash
set -e

echo "Setting up your environment for the Uno WebAssembly Bootstrapper. You may be requested to enter your password."

sudo apt install -y	ninja-build lbzip2

echo "Installing .NET"
sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-7.0

echo "Done."
