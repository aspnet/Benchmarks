
plaintextJobs="-j /benchmarks/src/Benchmarks/benchmarks.plaintext.json"
plaintextPlatformJobs="-j /benchmarks/src/PlatformBenchmarks/benchmarks.plaintext.json"
htmlJobs="-j /benchmarks/src/Benchmarks/benchmarks.html.json"
jsonJobs="-j /benchmarks/src/Benchmarks/benchmarks.json.json"
jsonPlatformJobs="-j /benchmarks/src/PlatformBenchmarks/benchmarks.json.json"
multiQueryJobs="-j /benchmarks/src/Benchmarks/benchmarks.multiquery.json"
signalRJobs="-j https://raw.githubusercontent.com/aspnet/SignalR/dev/benchmarks/BenchmarkServer/signalr.json -t SignalR -r signalr --projectFile benchmarks/BenchmarkServer/BenchmarkServer.csproj"

trend="--description \"Trend/Latest\""
baseLine="--description \"Baseline\" --aspnetCoreVersion Current --runtimeVersion Current"

jobs=(
  # Plaintext
  "-n PlaintextPlatform --webHost KestrelLibuv $trend $plaintextLibuvThreadCount $plaintextPlatformJobs"
  "-n PlaintextPlatform --webHost KestrelSockets $trend $plaintextPlatformJobs"
#   "-n Plaintext --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs" 
#   "-n Plaintext --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs" $baseLine
#   "-n Plaintext --webHost KestrelSockets $plaintextJobs" 
#   "-n Plaintext --webHost KestrelSockets $plaintextJobs $baseLine$"
#   "-n MvcPlaintext --webHost KestrelSockets $plaintextJobs" 
#   "-n MvcPlaintext --webHost KestrelLibuv $plaintextJobs" 
#   "-n Plaintext --webHost HttpSys $plaintextJobs" 
#   "-n Plaintext --webHost KestrelLibuv -f Benchmarks.PassthroughConnectionFilter $plaintextJobs" 
#   "-n StaticFiles --webHost KestrelLibuv --path plaintext $plaintextJobs" 
#   "-n JsonPlatform --webHost KestrelSockets $jsonPlatformJobs" 
#   "-n JsonPlatform --webHost KestrelLibuv $jsonPlatformJobs" 
#   "-n Json --webHost KestrelSockets $jsonJobs" 
#   "-n Json --webHost KestrelSockets $jsonJobs $baseLine"
#   "-n Json --webHost KestrelLibuv $jsonJobs"
#   "-n Json --webHost KestrelLibuv $jsonJobs $baseLine"
#   "-n Jil --webHost KestrelLibuv $jsonJobs"
#   "-n MvcJson --webHost KestrelSockets $jsonJobs" 
#   "-n MvcJson --webHost KestrelLibuv $jsonJobs" 
#   "-n MvcJil --webHost KestrelLibuv $jsonJobs" 

#   # Https
#   "-n Plaintext -m https --webHost KestrelSockets $plaintextJobs"
#   "-n Plaintext -m https --webHost KestrelLibuv $plaintextJobs"
#   "-n Plaintext -m https --webHost HttpSys $plaintextJobs"
#   "-n Json -m https --webHost KestrelSockets $jsonJobs"
#   "-n Json -m https --webHost KestrelLibuv $jsonJobs"
#   "-n Json -m https --webHost HttpSys $jsonJobs"

#   # Caching
#   "-n MemoryCachePlaintext --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs"
#   "-n MemoryCachePlaintextSetRemove --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs"
#   "-n ResponseCachingPlaintextCached --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs"
#   "-n ResponseCachingPlaintextCached --webHost KestrelLibuv $plaintextLibuvThreadCount --method DELETE $plaintextJobs"
#   "-n ResponseCachingPlaintextResponseNoCache --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs"
#   "-n ResponseCachingPlaintextRequestNoCache --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs"
#   "-n ResponseCachingPlaintextVaryByCached --webHost KestrelLibuv $plaintextLibuvThreadCount $plaintextJobs"

#   # Database SingleQuery
#   "-n DbSingleQueryRaw --webHost KestrelLibuv $jsonJobs --database PostgreSql"
#   "-n DbSingleQueryDapper --webHost KestrelLibuv $jsonJobs --database PostgreSql"
#   "-n DbSingleQueryMongoDb --webHost KestrelLibuv $jsonJobs --database MongoDb"
#   "-n DbSingleQueryEf --webHost KestrelLibuv $jsonJobs --database PostgreSql"
#   "-n MvcDbSingleQueryRaw --webHost KestrelLibuv $jsonJobs --database PostgreSql"
#   "-n MvcDbSingleQueryDapper --webHost KestrelLibuv $jsonJobs --database PostgreSql"
#   "-n MvcDbSingleQueryEf --webHost KestrelLibuv $jsonJobs --database PostgreSql"

#   # Database MultiQuery
#   "-n DbMultiQueryRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n DbMultiQueryDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n DbMultiQueryMongoDb --webHost KestrelLibuv $multiQueryJobs --database MongoDb"
#   "-n DbMultiQueryEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n MvcDbMultiQueryRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n MvcDbMultiQueryDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n MvcDbMultiQueryEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"

#   # Database MultiUpdate
#   "-n DbMultiUpdateRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n DbMultiUpdateDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n DbMultiUpdateEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n MvcDbMultiUpdateRaw --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n MvcDbMultiUpdateDapper --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"
#   "-n MvcDbMultiUpdateEf --webHost KestrelLibuv $multiQueryJobs --database PostgreSql"

#   # Database Fortunes
#   "-n DbFortunesRaw --webHost KestrelLibuv $htmlJobs --database PostgreSql"
#   "-n DbFortunesRaw --webHost KestrelLibuv $htmlJobs --database PostgreSql $baseLine"
#   "-n DbFortunesDapper --webHost KestrelLibuv $htmlJobs --database PostgreSql"
#   "-n DbFortunesMongoDb --webHost KestrelLibuv $htmlJobs --database MongoDb"
#   "-n DbFortunesEf --webHost KestrelLibuv $htmlJobs --database PostgreSql"
#   "-n DbFortunesEf --webHost KestrelLibuv $htmlJobs --database PostgreSql $baseLine"
#   "-n MvcDbFortunesRaw --webHost KestrelLibuv $htmlJobs --database PostgreSql"
#   "-n MvcDbFortunesDapper --webHost KestrelLibuv $htmlJobs --database PostgreSql"
#   "-n MvcDbFortunesEf --webHost KestrelLibuv $htmlJobs --database PostgreSql"

#   # SignalR
#   "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=json $signalRJobs"
#   "-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=messagepack $signalRJobs"
#   "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=json $signalRJobs"
#   "-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=messagepack $signalRJobs"
#   "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=json $signalRJobs"
#   "-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=messagepack $signalRJobs"
)

while [ $# -ne 0 ]
do
    name="$1"
    case "$name" in
        --sql)
            shift
            sql="-q \"$1\""
            ;;
        -s|--server)
            shift
            server="$1"
            ;;
        -c|--client)
            shift
            client="$1"
            ;;
        -p|--plaintextLibuvThreadCount)
            shift
            plaintextLibuvThreadCount="--kestrelThreadCount $1"
            ;;
        *)
            say_err "Unknown argument \`$name\`"
            exit 1
            ;;
    esac

    shift
done

if [ -z "$server" ]
then
    echo "-s|--server needs to be set"
    exit 1
fi

if [ -z "$client" ]
then
    echo "-c|--client needs to be set"
    exit 1
fi

if [ -z "$plaintextLibuvThreadCount" ]
then
    echo "-p|--plaintextLibuvThreadCount needs to be set"
    exit 1
fi

for s in ${server//,/ }
do
    for job in "${jobs[@]}"
    do
    dotnet /benchmarks/src/BenchmarksDriver/published/BenchmarksDriver.dll -s "$s" -c "$client" $sql $job
    done
done
