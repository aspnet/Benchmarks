New-Item -ItemType Directory -Force -Path .\.build
wget "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1" -OutFile ".\.build\dotnet-install.ps1"
.\.build\dotnet-install.ps1 -Channel 2.1.300 -SkipNonVersionedFiles -Version 2.1.300
dotnet build -c Release