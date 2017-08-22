#echo on
set -x

docker run -it --rm --network host benchmarks /root/.dotnet/dotnet /Benchmarks/src/BenchmarksClient/bin/Debug/netcoreapp2.0/BenchmarksClient.dll
