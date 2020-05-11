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

plaintextJobs="-j $ROOT/src/Benchmarks/benchmarks.plaintext.json"
htmlJobs="-j $ROOT/src/Benchmarks/benchmarks.html.json"
jsonJobs="-j $ROOT/src/Benchmarks/benchmarks.json.json"
multiQueryJobs="-j $ROOT/src/Benchmarks/benchmarks.multiquery.json"
httpClientJobs="-j $ROOT/src/BenchmarksApps/HttpClient/Proxy/benchmarks.json"
plaintextPlatformJobs="-j $ROOT/src/BenchmarksApps/Kestrel/PlatformBenchmarks/benchmarks.plaintext.json"
jsonPlatformJobs="-j $ROOT/src/BenchmarksApps/Kestrel/PlatformBenchmarks/benchmarks.json.json"
basicApiJobs="--database MySql --jobs https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Mvc/benchmarkapps/BasicApi/benchmarks.json --duration 60"
basicViewsJobs="--database MySql --jobs https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/Mvc/benchmarkapps/BasicViews/benchmarks.json --duration 60"
http2Jobs="--clientName H2Load -p Streams=70 --headers None --connections $CPU_COUNT --clientThreads $CPU_COUNT"
blazorJobs="-j $ROOT/src/Benchmarks/benchmarks.blazor.json"

trend="--description Trend/Latest"
plaintextLibuvThreadCount="--kestrelThreadCount $PLAINTEXT_LIBUV_THREAD_COUNT"

jobs=(
  # Plaintext
  "-n PlaintextPlatform --webHost KestrelSockets $trend $plaintextPlatformJobs"
  "-n Plaintext --webHost KestrelSockets $trend $plaintextJobs -i 3"
  "-n PlaintextNonPipelined --webHost KestrelSockets $trend $plaintextJobs"
  "-n MvcPlaintext --webHost KestrelSockets $trend $plaintextJobs -i 3"
  "-n EndpointPlaintext --webHost KestrelSockets $trend $plaintextJobs -i 3"
  "-n Plaintext --webHost HttpSys $trend $plaintextJobs --windows-only"
  "-n StaticFiles --webHost Kestrelsockets --path plaintext $trend $plaintextJobs -i 3"

  # Json
  "-n JsonPlatform --webHost KestrelSockets $trend $jsonPlatformJobs"
  "-n Json --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJson --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJsonNet --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJson2k --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJsonNet2k --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJsonInput2k --webHost KestrelSockets $trend $jsonJobs"
  "-n MvcJsonNetInput2k --webHost KestrelSockets $trend $jsonJobs"

  # Https
  "-n Plaintext -m https --webHost KestrelSockets $trend $plaintextJobs"
  "-n Plaintext -m https --webHost HttpSys $trend $plaintextJobs --windows-only"
  "-n Json -m https --webHost KestrelSockets $trend $jsonJobs"
  "-n Json -m https --webHost HttpSys $trend $jsonJobs --windows-only"
  "-n PlaintextNonPipelined -m https --webHost KestrelSockets $trend $plaintextJobs"

  # Http2
  "-n PlaintextNonPipelined --webHost KestrelSockets $trend $plaintextJobs $http2Jobs -m h2"
  "-n PlaintextNonPipelined --webHost HttpSys $trend $plaintextJobs $http2Jobs -m h2 --windows-only"
  "-n PlaintextNonPipelined --webHost KestrelSockets $trend $plaintextJobs $http2Jobs -m h2c --no-startup-latency" # no startup time as h2c is not supported by HttpClient
  "-n Json --webHost KestrelSockets $trend $jsonJobs $http2Jobs -m h2"
  "-n Json --webHost HttpSys $trend $jsonJobs $http2Jobs -m h2 --windows-only"
  "-n Json --webHost KestrelSockets $trend $jsonJobs $http2Jobs -m h2c --no-startup-latency" # no startup time as h2c is not supported by HttpClient

  # Caching
  "-n MemoryCachePlaintext --webHost KestrelSockets $trend $plaintextJobs"
  "-n MemoryCachePlaintextSetRemove --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextCached --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextResponseNoCache --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextRequestNoCache --webHost KestrelSockets $trend $plaintextJobs"
  "-n ResponseCachingPlaintextVaryByCached --webHost KestrelSockets $trend $plaintextJobs"

  # Database SingleQuery
  "-n DbSingleQueryRaw --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n DbSingleQueryDapper --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  #"-n DbSingleQueryMongoDb --webHost KestrelSockets $trend $jsonJobs --database MongoDb"
  "-n DbSingleQueryEf --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryRaw --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryDapper --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"
  "-n MvcDbSingleQueryEf --webHost KestrelSockets $trend $jsonJobs --database PostgreSql"

  # Database MultiQuery
  "-n DbMultiQueryRaw --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  "-n DbMultiQueryDapper --webHost KestrelSockets $trend $multiQueryJobs --database PostgreSql"
  #"-n DbMultiQueryMongoDb --webHost KestrelSockets $trend $multiQueryJobs --database MongoDb"
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
  #"-n DbFortunesMongoDb --webHost KestrelSockets $trend $htmlJobs --database MongoDb"
  "-n DbFortunesEf --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesRaw --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesDapper --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"
  "-n MvcDbFortunesEf --webHost KestrelSockets $trend $htmlJobs --database PostgreSql"

  # IIS
  "-n Plaintext --webHost IISInProcess $trend $plaintextJobs --windows-only"
  "-n Plaintext --webHost IISInProcess $trend $plaintextJobs $http2Jobs -m h2 --windows-only"
  "-n Plaintext --webHost IISOutOfProcess $trend $plaintextJobs --windows-only"
  "-n Plaintext --webHost IISOutOfProcess $trend $plaintextJobs $http2Jobs -m h2 --windows-only"
  "-n PlaintextNonPipelined --webHost IISInProcess $trend $plaintextJobs --windows-only"
  "-n PlaintextNonPipelined --webHost IISInProcess $trend $plaintextJobs $http2Jobs -m h2 --windows-only"
  "-n PlaintextNonPipelined --webHost IISOutOfProcess $trend $plaintextJobs --windows-only"
  "-n PlaintextNonPipelined --webHost IISOutOfProcess $trend $plaintextJobs $http2Jobs -m h2 --windows-only"
  "-n Json --webHost IISInProcess $trend $jsonJobs --windows-only"
  "-n Json --webHost IISInProcess $trend $jsonJobs $http2Jobs -m h2 --windows-only"
  "-n Json --webHost IISOutOfProcess $trend $jsonJobs --windows-only"
  "-n Json --webHost IISOutOfProcess $trend $jsonJobs $http2Jobs -m h2 --windows-only"

  # BasicApi
  # "--scenario BasicApi.GetUsingRouteValue $trend $basicApiJobs"
  # "--scenario BasicApi.Post $trend $basicApiJobs"

  # BasicViews
  # "--scenario BasicViews.GetTagHelpers $trend $basicViewsJobs"
  # "--scenario BasicViews.Post $trend $basicViewsJobs"

  # HttpClient
  "--scenario HttpClient $trend $httpClientJobs"

  # Connections
  "-n ConnectionClose --webHost KestrelSockets $trend $plaintextJobs --warmup 2 --duration 5"
  "-n ConnectionClose --webHost KestrelSockets $trend $plaintextJobs --warmup 2 --duration 5 -m https"

  # Logging
  "-n PlaintextNonPipelinedLogging --env ASPNETCORE_LogLevel=Warning $trend $plaintextJobs"
  "-n PlaintextNonPipelinedLoggingNoScopes --env ASPNETCORE_LogLevel=Warning --env ASPNETCORE_DisableScopes=true $trend $plaintextJobs"
  
  # Blazor
  "-n BlazorBasic $trend $blazorJobs"
  "-n BlazorFormInput $trend $blazorJobs"

  # Mono
  "-n PlaintextPlatform-Mono --webHost KestrelSockets $trend $plaintextPlatformJobs --mono-runtime"
  "-n Plaintext-Mono --webHost KestrelSockets $trend $plaintextJobs -i 3 --mono-runtime"
  "-n PlaintextNonPipelined-Mono --webHost KestrelSockets $trend $plaintextJobs --mono-runtime"
  "-n JsonPlatform-Mono --webHost KestrelSockets $trend $jsonPlatformJobs --mono-runtime"
  "-n Json-Mono --webHost KestrelSockets $trend $jsonJobs --mono-runtime"
  "-n Plaintext-Mono -m https --webHost KestrelSockets $trend $plaintextJobs --mono-runtime"
  "-n DbFortunesRaw-Mono --webHost KestrelSockets $trend $htmlJobs --database PostgreSql --mono-runtime"

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
