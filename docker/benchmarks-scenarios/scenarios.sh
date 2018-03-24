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
plaintextLibuvThreadCount="--kestrelThreadCount $PLAINTEXT_LIBUV_THREAD_COUNT"

jobs=(
  # Plaintext
  "-n PlaintextPlatform --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextPlatformJobs"
  "-n PlaintextPlatform --webHost KestrelSockets $trend $plaintextPlatformJobs"
  "-n Plaintext --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n Plaintext --webHost KestrelSockets $trend $plaintextJobs" 
  "-n Plaintext --webHost KestrelSockets $baseline $plaintextJobs$"
  "-n MvcPlaintext --webHost KestrelSockets $trend $plaintextJobs" 
  "-n MvcPlaintext --webHost KestrelLibuv $trend $plaintextJobs" 
  "-n Plaintext --webHost HttpSys $trend $plaintextJobs --windows-only" 
  "-n Plaintext --webHost KestrelLibuv -f Benchmarks.PassthroughConnectionFilter $trend $plaintextJobs" 
  "-n StaticFiles --webHost KestrelLibuv --path plaintext $trend $plaintextJobs" 
  "-n JsonPlatform --webHost KestrelSockets $trend $jsonPlatformJobs" 
  "-n JsonPlatform --webHost KestrelLibuv $trend $jsonPlatformJobs" 
  "-n Json --webHost KestrelSockets $trend $jsonJobs" 
  "-n Json --webHost KestrelSockets $baseline $jsonJobs"
  "-n Json --webHost KestrelLibuv $trend $jsonJobs"
  "-n Json --webHost KestrelLibuv $baseline $jsonJobs"
  "-n Jil --webHost KestrelLibuv $trend $jsonJobs"
  "-n MvcJson --webHost KestrelSockets $trend $jsonJobs" 
  "-n MvcJson --webHost KestrelLibuv $trend $jsonJobs" 
  "-n MvcJil --webHost KestrelLibuv $trend $jsonJobs" 

  # Https
  "-n Plaintext -m https --webHost KestrelSockets $trend $plaintextJobs"
  "-n Plaintext -m https --webHost KestrelLibuv $trend $plaintextJobs"
  "-n Plaintext -m https --webHost HttpSys $trend $plaintextJobs --windows-only"
  "-n Json -m https --webHost KestrelSockets $trend $jsonJobs"
  "-n Json -m https --webHost KestrelLibuv $trend $jsonJobs"
  "-n Json -m https --webHost HttpSys $trend $jsonJobs --windows-only"

  # Caching
  "-n MemoryCachePlaintext --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n MemoryCachePlaintextSetRemove --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n ResponseCachingPlaintextCached --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n ResponseCachingPlaintextCached --webHost KestrelLibuv $trend $plaintextLibuvThreadCount --method DELETE $plaintextJobs"
  "-n ResponseCachingPlaintextResponseNoCache --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n ResponseCachingPlaintextRequestNoCache --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n ResponseCachingPlaintextVaryByCached --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"

  # Database SingleQuery
  "-n DbSingleQueryRaw --webHost KestrelLibuv $trend $jsonJobs --database PostgreSql"
  "-n DbSingleQueryDapper --webHost KestrelLibuv $trend $jsonJobs --database PostgreSql"
  "-n DbSingleQueryMongoDb --webHost KestrelLibuv $trend $jsonJobs --database MongoDb"
  "-n DbSingleQueryEf --webHost KestrelLibuv $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryRaw --webHost KestrelLibuv $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryDapper --webHost KestrelLibuv $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryEf --webHost KestrelLibuv $trend $jsonJobs --database PostgreSql"

  # Database MultiQuery
  "-n DbMultiQueryRaw --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryDapper --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryMongoDb --webHost KestrelLibuv $trend $multiQueryJobs --database MongoDb"
  "-n DbMultiQueryEf --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryRaw --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryDapper --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryEf --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"

  # Database MultiUpdate
  "-n DbMultiUpdateRaw --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiUpdateDapper --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiUpdateEf --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateRaw --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateDapper --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateEf --webHost KestrelLibuv $trend $multiQueryJobs --database PostgreSql"

  # Database Fortunes
  "-n DbFortunesRaw --webHost KestrelLibuv $trend $htmlJobs --database PostgreSql"
  "-n DbFortunesRaw --webHost KestrelLibuv $baseline $htmlJobs --database PostgreSql"
  "-n DbFortunesDapper --webHost KestrelLibuv $trend $htmlJobs --database PostgreSql"
  "-n DbFortunesMongoDb --webHost KestrelLibuv $trend $htmlJobs --database MongoDb"
  "-n DbFortunesEf --webHost KestrelLibuv $trend $htmlJobs --database PostgreSql"
  "-n DbFortunesEf --webHost KestrelLibuv $baseline $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesRaw --webHost KestrelLibuv $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesDapper --webHost KestrelLibuv $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesEf --webHost KestrelLibuv $trend $htmlJobs --database PostgreSql"

  # IIS
  "-n Plaintext --webHost IISInProcess $trend $plaintextJobs --windows-only"
  "-n Plaintext --webHost IISOutOfProcess $trend $plaintextJobs --windows-only"
  "-n Json --webHost IISInProcess $trend $jsonJobs --windows-only"
  "-n Json --webHost IISOutOfProcess $trend $jsonJobs --windows-only"

  # SignalR
  "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=json $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=json $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=json $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=messagepack $trend $signalRJobs"
)

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet /benchmarks/src/BenchmarksDriver/published/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS
        # error code in $?
    done
done
