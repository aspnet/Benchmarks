if [ -z "$BENCHMARKS_SERVER" ]
then
    echo "\$BENCHMARKS_SERVER is not set"
    exit 1
fi

if [ -z "$BENCHMARKS_CLIENT" ]
then
    echo "\$BENCHMARKS_CLIENT is not set"
    exit 1
fi

if [ -z "$BENCHMARKS_SQL" ]
then
    echo "\$BENCHMARKS_SQL is not set"
    exit 1
fi

if [ -z "$CPU_COUNT" ]
then
    echo "\$CPU_COUNT is not set"
    exit 1
fi

# compute current directory
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT=$DIR/..
SESSION=`date '+%Y%m%d%H%M%S'`

grpcNativeJobs="-j https://raw.githubusercontent.com/grpc/grpc-dotnet/master/perf/benchmarkapps/GrpcCoreServer/grpc-core.json"
grpcManagedJobs="-j https://raw.githubusercontent.com/grpc/grpc-dotnet/master/perf/benchmarkapps/GrpcAspNetCoreServer/grpc-aspnetcore.json"
grpcGoJobs="-j $ROOT/src/BenchmarksApps/Grpc/GoServer/grpc-go.json"
grpcHttpApiJobs="-j $ROOT/src/BenchmarksApps/Grpc/GrpcHttpApiServer/grpc-httpapi.json"

trend="--description Trend/Latest"

jobs=(

  # GRPC ASP.NET Core
  "-n GrpcUnary-h2load --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c --headers None"
  "-n GrpcUnary-h2load --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2 --headers None"
  "-n GrpcUnary-Streams-h2load --webHost KestrelSockets $trend $grpcManagedJobs -p Streams=70 --connections $CPU_COUNT --clientThreads $CPU_COUNT --warmup 5 -m h2c --headers None"
  "-n GrpcUnary-Streams-h2load --webHost KestrelSockets $trend $grpcManagedJobs -p Streams=70 --connections $CPU_COUNT --clientThreads $CPU_COUNT --warmup 5 -m h2 --headers None"
  "-n GrpcUnary-GrpcCore --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2"
  "-n GrpcUnary-1MB-GrpcCore --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-1MB-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-1MB-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2"
  "-n GrpcServerStreaming-GrpcCore --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcServerStreaming-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcServerStreaming-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2"
  "-n GrpcPingPongStreaming-GrpcCore --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2"
  "-n GrpcPingPongStreaming-1MB-GrpcCore --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-1MB-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-1MB-GrpcNetClient --webHost KestrelSockets $trend $grpcManagedJobs --connections 64 --warmup 5 -m h2"

  # GRPC C-core
  "-n GrpcUnary-h2load --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c --headers None"
  "-n GrpcUnary-Streams-h2load --webHost CCore $trend $grpcNativeJobs -p Streams=70 --connections $CPU_COUNT --clientThreads $CPU_COUNT --warmup 5 -m h2c --headers None"
  "-n GrpcUnary-GrpcCore --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-GrpcNetClient --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-1MB-GrpcCore --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcUnary-1MB-GrpcNetClient --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcServerStreaming-GrpcCore --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcServerStreaming-GrpcNetClient --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-GrpcCore --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-GrpcNetClient --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-1MB-GrpcCore --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"
  "-n GrpcPingPongStreaming-1MB-GrpcNetClient --webHost CCore $trend $grpcNativeJobs --connections 64 --warmup 5 -m h2c"

  # GRPC Go
  "-n GrpcUnary-h2load --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --headers None --linux-only"
  "-n GrpcUnary-h2load --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2 --env protocol=h2 --headers None --linux-only"
  "-n GrpcUnary-Streams-h2load --webHost Docker $trend $grpcGoJobs -p Streams=70 --connections $CPU_COUNT --clientThreads $CPU_COUNT --warmup 5 -m h2c --headers None --linux-only"
  "-n GrpcUnary-Streams-h2load --webHost Docker $trend $grpcGoJobs -p Streams=70 --connections $CPU_COUNT --clientThreads $CPU_COUNT --warmup 5 -m h2 --headers None --linux-only"
  "-n GrpcUnary-GrpcCore --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcUnary-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcUnary-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2 --env protocol=h2 --linux-only"
  "-n GrpcUnary-1MB-GrpcCore --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcUnary-1MB-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcUnary-1MB-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2 --env protocol=h2 --linux-only"
  "-n GrpcServerStreaming-GrpcCore --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcServerStreaming-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcServerStreaming-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2 --env protocol=h2 --linux-only"
  "-n GrpcPingPongStreaming-GrpcCore --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcPingPongStreaming-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcPingPongStreaming-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2 --env protocol=h2 --linux-only"
  "-n GrpcPingPongStreaming-1MB-GrpcCore --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcPingPongStreaming-1MB-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2c --linux-only"
  "-n GrpcPingPongStreaming-1MB-GrpcNetClient --webHost Docker $trend $grpcGoJobs --connections 64 --warmup 5 -m h2 --env protocol=h2 --linux-only"

  # GRPC HTTP API
  "-n GrpcJson --webHost KestrelSockets $trend $grpcHttpApiJobs -m http"
  "-n GrpcJson --webHost KestrelSockets $trend $grpcHttpApiJobs -m https"
  "-n GrpcJson2k --webHost KestrelSockets $trend $grpcHttpApiJobs -m http"
  "-n GrpcJson2k --webHost KestrelSockets $trend $grpcHttpApiJobs -m https"
  "-n GrpcJsonInput2k --webHost KestrelSockets $trend $grpcHttpApiJobs -m http"
  "-n GrpcJsonInput2k --webHost KestrelSockets $trend $grpcHttpApiJobs -m https"
)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS --self-contained --collect-counters
        # error code in $?
    done
done
