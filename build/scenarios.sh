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

if [ -z "$CPU_COUNT" ]
then
    echo "\$CPU_COUNT is not set"
    exit 1
fi

# compute current directory
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT=$DIR/..
SESSION=`date '+%Y%m%d%H%M%S'`

plaintextJobs="-j $ROOT/src/Benchmarks/benchmarks.plaintext.json"
htmlJobs="-j $ROOT/src/Benchmarks/benchmarks.html.json"
jsonJobs="-j $ROOT/src/Benchmarks/benchmarks.json.json"
multiQueryJobs="-j $ROOT/src/Benchmarks/benchmarks.multiquery.json"
httpClientJobs="-j $ROOT/src/Benchmarks/benchmarks.httpclient.json"
signalRJobs="-j https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/SignalR/benchmarkapps/BenchmarkServer/signalr.json -t SignalR"
plaintextPlatformJobs="-j https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Servers/Kestrel/perf/PlatformBenchmarks/benchmarks.plaintext.json"
jsonPlatformJobs="-j https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Servers/Kestrel/perf/PlatformBenchmarks/benchmarks.json.json"
routingJobs="-j https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Routing/benchmarkapps/Benchmarks/benchmarks.json"
basicApiJobs="--database MySql --jobs https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Mvc/benchmarkapps/BasicApi/benchmarks.json --duration 60"
basicViewsJobs="--database MySql --jobs https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Mvc/benchmarkapps/BasicViews/benchmarks.json --duration 60"
http2Jobs="--clientName H2Load -p Streams=70 --headers None --connections $CPU_COUNT --clientThreads $CPU_COUNT"

trend="--description Trend/Latest"
plaintextLibuvThreadCount="--kestrelThreadCount $PLAINTEXT_LIBUV_THREAD_COUNT"

routingBenchmarks="-r AspNetCore@master --projectFile src/Routing/benchmarks/Microsoft.AspNetCore.Routing.Performance/Microsoft.AspNetCore.Routing.Performance.csproj --no-arguments --description Benchmarks --arg --config=perflab --server-timeout 00:20:00"

jobs=(
  # Plaintext
  "-n PlaintextPlatform --webHost KestrelLibuv $trend $plaintextPlatformJobs"
  "-n PlaintextPlatform --webHost KestrelSockets $trend $plaintextPlatformJobs"
  "-n Plaintext --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextJobs"
  "-n Plaintext --webHost KestrelSockets $trend $plaintextJobs -i 3" 
  "-n PlaintextNonPipelined --webHost KestrelSockets $trend $plaintextJobs" 
  "-n MvcPlaintext --webHost KestrelSockets $trend $plaintextJobs -i 3" 
  "-n MvcPlaintext --webHost KestrelLibuv $trend $plaintextJobs" 
  "-n Plaintext --webHost HttpSys $trend $plaintextJobs --windows-only" 
  "-n Plaintext --webHost KestrelSockets -cf Benchmarks.PassthroughConnectionFilter $trend $plaintextJobs" 
  "-n StaticFiles --webHost Kestrelsockets --path plaintext $trend $plaintextJobs -i 3" 
  "-n JsonPlatform --webHost KestrelSockets $trend $jsonPlatformJobs" 
  "-n JsonPlatform --webHost KestrelLibuv $trend $jsonPlatformJobs" 
  "-n Json --webHost KestrelSockets $trend $jsonJobs" 
  "-n Json --webHost KestrelLibuv $trend $jsonJobs"
  "-n Jil --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJson --webHost KestrelSockets $trend $jsonJobs" 
  "-n MvcJson --webHost KestrelLibuv $trend $jsonJobs" 
  "-n MvcJil --webHost KestrelSockets $trend $jsonJobs" 
  "-n PlaintextRouting --webHost KestrelSockets $trend $routingJobs" 
  "-n PlaintextDispatcher --webHost KestrelSockets $trend $routingJobs" 

  # Https
  "-n Plaintext -m https --webHost KestrelSockets $trend $plaintextJobs"
  "-n Plaintext -m https --webHost KestrelLibuv $trend $plaintextJobs"
  "-n Plaintext -m https --webHost HttpSys $trend $plaintextJobs --windows-only"
  "-n Json -m https --webHost KestrelSockets $trend $jsonJobs"
  "-n Json -m https --webHost KestrelLibuv $trend $jsonJobs"
  "-n Json -m https --webHost HttpSys $trend $jsonJobs --windows-only"
  "-n PlaintextNonPipelined -m https --webHost KestrelSockets $trend $plaintextJobs"
  
  # Http2
  "-n PlaintextNonPipelined --webHost KestrelSockets $trend $plaintextJobs $http2Jobs -m h2"
  "-n PlaintextNonPipelined --webHost KestrelSockets $trend $plaintextJobs $http2Jobs -m h2c"
  "-n Json --webHost KestrelSockets $trend $jsonJobs $http2Jobs -m h2"
  "-n Json --webHost KestrelSockets $trend $jsonJobs $http2Jobs -m h2c"

  # Caching
  "-n MemoryCachePlaintext --webHost KestrelSockets $trend $plaintextJobs"
  "-n MemoryCachePlaintextSetRemove --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextCached --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextCachedDelete --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextResponseNoCache --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextRequestNoCache --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextVaryByCached --webHost KestrelSockets $trend $plaintextJobs"

  # Database SingleQuery
  "-n DbSingleQueryRaw --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n DbSingleQueryDapper --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n DbSingleQueryMongoDb --webHost KestrelSockets $trend $jsonJobs --database MongoDb"
  "-n DbSingleQueryEf --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryRaw --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryDapper --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryEf --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"

  # Database MultiQuery
  "-n DbMultiQueryRaw --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryDapper --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryMongoDb --webHost KestrelSockets $trend $multiQueryJobs --database MongoDb"
  "-n DbMultiQueryEf --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryRaw --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryDapper --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiQueryEf --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"

  # Database MultiUpdate
  "-n DbMultiUpdateRaw --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiUpdateDapper --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiUpdateEf --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateRaw --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateDapper --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n MvcDbMultiUpdateEf --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"

  # Database Fortunes
  "-n DbFortunesRaw --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n DbFortunesDapper --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n DbFortunesMongoDb --webHost KestrelSockets $trend $htmlJobs --database MongoDb"
  "-n DbFortunesEf --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesRaw --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesDapper --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesEf --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"

  # IIS
  "-n Plaintext --webHost IISInProcess $trend $plaintextJobs --windows-only"
  "-n Plaintext --webHost IISOutOfProcess $trend $plaintextJobs --windows-only"
  "-n PlaintextNonPipelined --webHost IISInProcess $trend $plaintextJobs --windows-only"
  "-n PlaintextNonPipelined --webHost IISOutOfProcess $trend $plaintextJobs --windows-only"
  "-n Json --webHost IISInProcess $trend $jsonJobs --windows-only"
  "-n Json --webHost IISOutOfProcess $trend $jsonJobs --windows-only"

  # SignalR
  "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=json $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=json $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=json $trend $signalRJobs"
  "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalREcho -p TransportType=WebSockets -p HubProtocol=json $trend $signalRJobs"
  "-n SignalREcho -p TransportType=WebSockets -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalREcho -p TransportType=ServerSentEvents -p HubProtocol=json $trend $signalRJobs"
  "-n SignalREcho -p TransportType=LongPolling -p HubProtocol=json $trend $signalRJobs"
  "-n SignalREcho -p TransportType=LongPolling -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalREchoAll -p TransportType=WebSockets -p HubProtocol=json $trend $signalRJobs"
  "-n SignalREchoAll -p TransportType=WebSockets -p HubProtocol=messagepack $trend $signalRJobs"
  "-n SignalREchoAll -p TransportType=ServerSentEvents -p HubProtocol=json $trend $signalRJobs"
  "-n SignalREchoAll -p TransportType=LongPolling -p HubProtocol=json $trend $signalRJobs"
  "-n SignalREchoAll -p TransportType=LongPolling -p HubProtocol=messagepack $trend $signalRJobs"

  # BasicApi
  "--scenario BasicApi.GetUsingRouteValue $trend $basicApiJobs"
  "--scenario BasicApi.Post $trend $basicApiJobs"

  # BasicViews
  "--scenario BasicViews.GetTagHelpers $trend $basicViewsJobs"
  "--scenario BasicViews.Post $trend $basicViewsJobs"

  # HttpClient
  "--scenario HttpClient $trend $httpClientJobs"
  "--scenario HttpClientParallel $trend $httpClientJobs"
  "--scenario HttpClientFactory $trend $httpClientJobs --duration 20" # custom duration to ensure the DNS refresh is exercized

  # Routing Benchmarks
  "$routingBenchmarks --benchmarkdotnet RouteValueDictionaryBenchmark --arg RouteValueDictionaryBenchmark"
  "$routingBenchmarks --benchmarkdotnet LinkGenerationGithubBenchmark --arg LinkGenerationGithubBenchmark"
  "$routingBenchmarks --benchmarkdotnet MatcherAzureBenchmark --arg MatcherAzureBenchmark"
  "$routingBenchmarks --benchmarkdotnet MatcherBuilderAzureBenchmark --arg MatcherBuilderAzureBenchmark"
  "$routingBenchmarks --benchmarkdotnet MatcherSingleEntryBenchmark --arg MatcherSingleEntryBenchmark"
)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS
        # error code in $?
    done
done
