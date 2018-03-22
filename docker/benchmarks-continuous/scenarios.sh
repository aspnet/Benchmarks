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

plaintextJobs="-j /benchmarks/src/Benchmarks/benchmarks.plaintext.json"
plaintextPlatformJobs="-j /benchmarks/src/PlatformBenchmarks/benchmarks.plaintext.json"
htmlJobs="-j /benchmarks/src/Benchmarks/benchmarks.html.json"
jsonJobs="-j /benchmarks/src/Benchmarks/benchmarks.json.json"
jsonPlatformJobs="-j /benchmarks/src/PlatformBenchmarks/benchmarks.json.json"
multiQueryJobs="-j /benchmarks/src/Benchmarks/benchmarks.multiquery.json"
signalRJobs="-j https://raw.githubusercontent.com/aspnet/SignalR/dev/benchmarks/BenchmarkServer/signalr.json -t SignalR -r signalr --projectFile benchmarks/BenchmarkServer/BenchmarkServer.csproj"

trendJob="--description \"Trend/Latest\""
baseLineJob="--description \"Trend/Latest\" --aspnetCoreVersion Current --runtimeVersion Current"

jobs=(
  # Plaintext
  "-n PlaintextPlatform --webHost KestrelSockets $trendJob $plaintextPlatformJobs"
  "-n PlaintextPlatform --webHost KestrelLibuv $trendJob $plaintextPlatformJobs $plaintextLibuvThreadCount"
)

for job in "${jobs[@]}"
do
   dotnet /benchmarks/src/BenchmarksDriver/published/BenchmarksDriver.dll -s \"$server\" -c \"$client\" $sql $job
done


    #PlaintextThreadCount = "--kestrelThreadCount 2"

#   Description = "Trend/Latest"
#   AspNetCoreVersion="Latest"
#   RuntimeVersion="Latest"

    # Plaintext
#     <Scenarios Include="-n PlaintextPlatform --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextPlatformJobs)" />
#     <Scenarios Include="-n PlaintextPlatform --webHost KestrelSockets $(PlaintextPlatformJobs)" />
#     <Scenarios Include="-n Plaintext --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />
#     <Scenarios Include="-n Plaintext --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" Description="Baseline" AspNetCoreVersion="Current" RuntimeVersion="Current"/>
#     <Scenarios Include="-n Plaintext --webHost KestrelSockets $(PlaintextJobs)" />
#     <Scenarios Include="-n Plaintext --webHost KestrelSockets $(PlaintextJobs)" Description="Baseline" AspNetCoreVersion="Current" RuntimeVersion="Current"/>
#     <Scenarios Include="-n MvcPlaintext --webHost KestrelSockets $(PlaintextJobs)" />
#     <Scenarios Include="-n MvcPlaintext --webHost KestrelLibuv $(PlaintextJobs)" />

#     <Scenarios Include="-n Plaintext --webHost HttpSys $(PlaintextJobs)" />
#     <Scenarios Include="-n Plaintext --webHost KestrelLibuv -f Benchmarks.PassthroughConnectionFilter $(PlaintextJobs)" />
#     <Scenarios Include="-n StaticFiles --webHost KestrelLibuv --path plaintext $(PlaintextJobs)" />

#     <Scenarios Include="-n JsonPlatform --webHost KestrelSockets $(JsonPlatformJobs)" />
#     <Scenarios Include="-n JsonPlatform --webHost KestrelLibuv $(JsonPlatformJobs)" />
#     <Scenarios Include="-n Json --webHost KestrelSockets $(JsonJobs)" />
#     <Scenarios Include="-n Json --webHost KestrelSockets $(JsonJobs)"  Description="Baseline" AspNetCoreVersion="Current" RuntimeVersion="Current"/>
#     <Scenarios Include="-n Json --webHost KestrelLibuv $(JsonJobs)" />
#     <Scenarios Include="-n Json --webHost KestrelLibuv $(JsonJobs)" Description="Baseline" AspNetCoreVersion="Current" RuntimeVersion="Current"/>
#     <Scenarios Include="-n Jil --webHost KestrelLibuv $(JsonJobs)" />
#     <Scenarios Include="-n MvcJson --webHost KestrelSockets $(JsonJobs)" />
#     <Scenarios Include="-n MvcJson --webHost KestrelLibuv $(JsonJobs)" />
#     <Scenarios Include="-n MvcJil --webHost KestrelLibuv $(JsonJobs)" />

#     <!-- Https -->
#     <Scenarios Include="-n Plaintext -m https --webHost KestrelSockets $(PlaintextJobs)" />
#     <Scenarios Include="-n Plaintext -m https --webHost KestrelLibuv $(PlaintextJobs)" />
#     <Scenarios Include="-n Plaintext -m https --webHost HttpSys $(PlaintextJobs)" />
#     <Scenarios Include="-n Json -m https --webHost KestrelSockets $(JsonJobs)" />
#     <Scenarios Include="-n Json -m https --webHost KestrelLibuv $(JsonJobs)" />
#     <Scenarios Include="-n Json -m https --webHost HttpSys $(JsonJobs)" />

#     <!-- Caching -->
#     <Scenarios Include="-n MemoryCachePlaintext --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />
#     <Scenarios Include="-n MemoryCachePlaintextSetRemove --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />

#     <Scenarios Include="-n ResponseCachingPlaintextCached --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />
#     <Scenarios Include="-n ResponseCachingPlaintextCached --webHost KestrelLibuv $(PlaintextThreadCount) --method DELETE $(PlaintextJobs)" />
#     <Scenarios Include="-n ResponseCachingPlaintextResponseNoCache --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />
#     <Scenarios Include="-n ResponseCachingPlaintextRequestNoCache --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />
#     <Scenarios Include="-n ResponseCachingPlaintextVaryByCached --webHost KestrelLibuv $(PlaintextThreadCount) $(PlaintextJobs)" />

#     <!-- Database SingleQuery -->
#     <Scenarios Include="-n DbSingleQueryRaw --webHost KestrelLibuv $(JsonJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbSingleQueryDapper --webHost KestrelLibuv $(JsonJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbSingleQueryMongoDb --webHost KestrelLibuv $(JsonJobs) --database MongoDb" />
#     <Scenarios Include="-n DbSingleQueryEf --webHost KestrelLibuv $(JsonJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbSingleQueryRaw --webHost KestrelLibuv $(JsonJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbSingleQueryDapper --webHost KestrelLibuv $(JsonJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbSingleQueryEf --webHost KestrelLibuv $(JsonJobs) --database PostgreSql" />

#     <!-- Database MultiQuery -->
#     <Scenarios Include="-n DbMultiQueryRaw --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbMultiQueryDapper --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbMultiQueryMongoDb --webHost KestrelLibuv $(MultiQueryJobs) --database MongoDb" />
#     <Scenarios Include="-n DbMultiQueryEf --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbMultiQueryRaw --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbMultiQueryDapper --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbMultiQueryEf --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />

#     <!-- Database MultiUpdate -->
#     <Scenarios Include="-n DbMultiUpdateRaw --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbMultiUpdateDapper --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbMultiUpdateEf --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbMultiUpdateRaw --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbMultiUpdateDapper --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbMultiUpdateEf --webHost KestrelLibuv $(MultiQueryJobs) --database PostgreSql" />

#     <!-- Database Fortunes -->
#     <Scenarios Include="-n DbFortunesRaw --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbFortunesRaw --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" Description="Baseline" AspNetCoreVersion="Current" RuntimeVersion="Current" />
#     <Scenarios Include="-n DbFortunesDapper --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbFortunesMongoDb --webHost KestrelLibuv $(HtmlJobs) --database MongoDb" />
#     <Scenarios Include="-n DbFortunesEf --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" />
#     <Scenarios Include="-n DbFortunesEf --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" Description="Baseline" AspNetCoreVersion="Current" RuntimeVersion="Current" />
#     <Scenarios Include="-n MvcDbFortunesRaw --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbFortunesDapper --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" />
#     <Scenarios Include="-n MvcDbFortunesEf --webHost KestrelLibuv $(HtmlJobs) --database PostgreSql" />

#     <!-- SignalR -->
#     <Scenarios Include="-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=json $(SignalRJobs)" />
#     <Scenarios Include="-n SignalRBroadcast -p TransportType=WebSockets -p HubProtocol=messagepack $(SignalRJobs)" />
#     <Scenarios Include="-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=json $(SignalRJobs)" />
#     <Scenarios Include="-n SignalRBroadcast -p TransportType=ServerSentEvents -p HubProtocol=messagepack $(SignalRJobs)" />
#     <Scenarios Include="-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=json $(SignalRJobs)" />
#     <Scenarios Include="-n SignalRBroadcast -p TransportType=LongPolling -p HubProtocol=messagepack $(SignalRJobs)" />
#   </ItemGroup>

