
# compute current directory
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT=$DIR/..
SESSION=`date '+%Y%m%d%H%M%S'`

proxyJobs="--config https://raw.githubusercontent.com/aspnet/Benchmarks/master/src/BenchmarksDriver2/benchmarks.proxy.yml "

jobs=(
  "--scenario proxy-baseline    $proxyJobs --property proxy=none"
  "--scenario proxy-httpclient  $proxyJobs --property proxy=httpclient"
  "--scenario proxy-nginx       $proxyJobs --property proxy=nginx"
  "--scenario proxy-haproxy     $proxyJobs --property proxy=haproxy"
  "--scenario proxy-envoy       $proxyJobs --property proxy=envoy"
)

payloads=(
    "10"
    "100"
    "1024"
    "10240"
    "102400"
)

protocols=(
    "http"
)

# build driver
cd $ROOT/src/BenchmarksDriver2
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver2

for job in "${jobs[@]}"
do
    for protocol in "${protocols[@]}"
    do
        for payload in "${payloads[@]}"
        do
            echo "New job: $job payload:$payload protocol:$protocol"
            dotnet $ROOT/.build/BenchmarksDriver2/BenchmarksDriver.dll $job --variable path=/?s=$payload --property payload=$payload --variable protocol=$protocol --property protocol=$protocol --no-metadata --no-measurements --warmup.options.discardResults true --session $SESSION --sql "$BENCHMARKS_SQL" --table ProxyBenchmarks $BENCHMARKS_ARGS

            # error code in $?
        done
    done
done
