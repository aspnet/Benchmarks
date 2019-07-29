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

signalRJobs="-j https://raw.githubusercontent.com/aspnet/AspNetCore/master/src/SignalR/perf/benchmarkapps/BenchmarkServer/signalr.json"
trend="--description Trend/Latest"

jobs=(
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
)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS --sdk latest --self-contained --collect-counters -t "SignalR"
        # error code in $?
    done
done
