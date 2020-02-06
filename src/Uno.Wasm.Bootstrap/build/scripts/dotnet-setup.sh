#!/bin/bash
set -e

echo "Setting up your environment for the Uno WebAssembly Bootstrapper. You may be requested to enter your password."

echo "Installing mono"
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys    3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list
sudo apt update

sudo apt install -y python mono-devel msbuild libc6 ninja-build

echo "Installing .NET Core"
wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb

sudo dpkg -i packages-microsoft-prod.deb
sudo add-apt-repository universe
sudo apt-get -y install apt-transport-https
sudo apt-get update
sudo apt-get -y install dotnet-sdk-3.0

echo "Done."
