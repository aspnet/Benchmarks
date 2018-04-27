#!/usr/bin/env bash
mkdir -p ./.build
wget -P ./.build/ "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.sh"
chmod +x ./dotnet-install.sh
./.build/dotnet-install.sh --channel 2.0 --skip-non-versioned-files
dotnet build -c Release
