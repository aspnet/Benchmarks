#!/usr/bin/env bash
mkdir -p ./.build
wget -P ./.build/ "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.sh"
chmod +x ./.build/dotnet-install.sh
./.build/dotnet-install.sh --channel 2.1.300 --skip-non-versioned-files --version 2.1.300
dotnet build -c Release
