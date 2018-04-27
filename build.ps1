mkdir .\.build
wget "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1" -OutFile ".\.build\dotnet-install.ps1"
.\.build\dotnet-install.ps1 -Channel 2.0 -SkipNonVersionedFiles
dotnet build -c Release