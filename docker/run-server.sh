#echo on
set -x

docker run -it --rm --mount type=bind,source=/mnt,target=/tmp --network host benchmarks /root/.dotnet/dotnet /Benchmarks/src/BenchmarksServer/bin/Debug/netcoreapp2.0/BenchmarksServer.dll -n $(ip route get 1 | awk '{print $NF;exit}')
