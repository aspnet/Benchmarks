
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

if [ -z "$PLAINTEXT_LIBUV_THREAD_COUNT" ]
then
    echo "\$PLAINTEXT_LIBUV_THREAD_COUNT is not set"
    exit 1
fi

plaintextJobs="-j /benchmarks/src/Benchmarks/benchmarks.plaintext.json"
plaintextPlatformJobs="-j /benchmarks/src/PlatformBenchmarks/benchmarks.plaintext.json"
htmlJobs="-j /benchmarks/src/Benchmarks/benchmarks.html.json"
jsonJobs="-j /benchmarks/src/Benchmarks/benchmarks.json.json"
jsonPlatformJobs="-j /benchmarks/src/PlatformBenchmarks/benchmarks.json.json"
multiQueryJobs="-j /benchmarks/src/Benchmarks/benchmarks.multiquery.json"
signalRJobs="-j https://raw.githubusercontent.com/aspnet/SignalR/dev/benchmarks/BenchmarkServer/signalr.json -t SignalR -r signalr --projectFile benchmarks/BenchmarkServer/BenchmarkServer.csproj"

trend="--description Trend/Latest"
baseline="--description Baseline --aspnetCoreVersion Current --runtimeVersion Current"

jobs=(
  # Plaintext
  "-n PlaintextPlatform --webHost KestrelLibuv $trend $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextPlatformJobs"
  "-n PlaintextPlatform --webHost KestrelSockets $trend $plaintextPlatformJobs"
  "-n Plaintext --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs" 
  "-n Plaintext --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs" $baseline
  "-n Plaintext --webHost KestrelSockets $plaintextJobs" 
  "-n Plaintext --webHost KestrelSockets $plaintextJobs $baseline$"
  "-n MvcPlaintext --webHost KestrelSockets $plaintextJobs" 
  "-n MvcPlaintext --webHost KestrelLibuv $plaintextJobs" 
  "-n Plaintext --webHost HttpSys $plaintextJobs" 
  "-n Plaintext --webHost KestrelLibuv -f Benchmarks.PassthroughConnectionFilter $plaintextJobs" 
  "-n StaticFiles --webHost KestrelLibuv --path plaintext $plaintextJobs" 
  "-n JsonPlatform --webHost KestrelSockets $jsonPlatformJobs" 
  "-n JsonPlatform --webHost KestrelLibuv $jsonPlatformJobs" 
  "-n Json --webHost KestrelSockets $jsonJobs" 
  "-n Json --webHost KestrelSockets $jsonJobs $baseline"
  "-n Json --webHost KestrelLibuv $jsonJobs"
  "-n Json --webHost KestrelLibuv $jsonJobs $baseline"
  "-n Jil --webHost KestrelLibuv $jsonJobs"
  "-n MvcJson --webHost KestrelSockets $jsonJobs" 
  "-n MvcJson --webHost KestrelLibuv $jsonJobs" 
  "-n MvcJil --webHost KestrelLibuv $jsonJobs" 

  # Https
  "-n Plaintext -m https --webHost KestrelSockets $plaintextJobs"
  "-n Plaintext -m https --webHost KestrelLibuv $plaintextJobs"
  "-n Plaintext -m https --webHost HttpSys $plaintextJobs"
  "-n Json -m https --webHost KestrelSockets $jsonJobs"
  "-n Json -m https --webHost KestrelLibuv $jsonJobs"
  "-n Json -m https --webHost HttpSys $jsonJobs"

  # Caching
  "-n MemoryCachePlaintext --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs"
  "-n MemoryCachePlaintextSetRemove --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs"
  "-n ResponseCachingPlaintextCached --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs"
  "-n ResponseCachingPlaintextCached --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT --method DELETE $plaintextJobs"
  "-n ResponseCachingPlaintextResponseNoCache --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs"
  "-n ResponseCachingPlaintextRequestNoCache --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs"
  "-n ResponseCachingPlaintextVaryByCached --webHost KestrelLibuv $PLAINTEXT_LIBUV_THREAD_COUNT $plaintextJobs"

  # Database SingleQuery
  "-n DbSingleQueryRaw --webHost KestrelLibuv $jsonJobs --database PostgreSql"
  "-n DbSingleQueryDapper --webHost KestrelLibuv $jsonJobs --database PostgreSql"
  "-n DbSingleQueryMongoDb --webHost KestrelLibuv $jsonJobs --database MongoDb"
  "-n DbSingleQueryEf --webHost KestrelLibuv $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryRaw --webHost KestrelLibuv $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryDapper --webHost KestrelLibuv $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryEf --webHost KestrelLibuv $jsonJobs --database PostgreSql"

  # Database MultiQuery
  "-n DbMultiQueryRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryMongoDb --webHost KestrelLibuv $multiQueryJobs --database MongoDb"
  "-n DbMultiQueryEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"

  # Database MultiUpdate
  "-n DbMultiUpdateRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n DbMultiUpdateDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n DbMultiUpdateEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"

  # Database Fortunes
  "-n DbFortunesRaw --webHost KestrelLibuv $htmlJobs --database PostgreSql"
  "-n DbFortunesRaw --webHost KestrelLibuv $htmlJobs --database PostgreSql $baseline"
  "-n DbFortunesDapper --webHost KestrelLibuv $htmlJobs --database PostgreSql"
  "-n DbFortunesMongoDb --webHost KestrelLibuv $htmlJobs --database MongoDb"
  "-n DbFortunesEf --webHost KestrelLibuv $htmlJobs --database PostgreSql"
  "-n DbFortunesEf --webHost KestrelLibuv $htmlJobs --database PostgreSql $baseline"
  "-n MvcDbFortunesRaw --webHost KestrelLibuv $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesDapper --webHost KestrelLibuv $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesEf --webHost KestrelLibuv $htmlJobs --database PostgreSql"

  # SignalR
  "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=json $signalRJobs"
  "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=messagepack $signalRJobs"
  "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=json $signalRJobs"
  "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=messagepack $signalRJobs"
  "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=json $signalRJobs"
  "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=messagepack $signalRJobs"
)

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        dotnet /benchmarks/src/BenchmarksDriver/published/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT -q \"$BENCHMARKS_SQL\" $job
    done
done
