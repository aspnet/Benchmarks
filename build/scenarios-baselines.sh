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

plaintextPlatformJobs="-j $ROOT/src/BenchmarksApps/Kestrel/PlatformBenchmarks/benchmarks.plaintext.json"
jsonPlatformJobs="-j $ROOT/src/BenchmarksApps/Kestrel/PlatformBenchmarks/benchmarks.json.json"
htmlPlatformJobs="-j $ROOT/src/BenchmarksApps/Kestrel/PlatformBenchmarks/benchmarks.html.json"

baselines=(
  # Stable 2.1
  "--description Baseline21 --aspnetCoreVersion 2.1 --runtimeVersion 2.1 " 
  
  # Servicing 2.1
  "--description Baseline21Servicing --aspnetCoreVersion 2.1.* --runtimeVersion 2.1.* "

  # Stable 2.2
  # "--description Baseline22 --aspnetCoreVersion 2.2 --runtimeVersion 2.2 "

  # Servicing 2.2
  # "--description Baseline22Servicing --aspnetCoreVersion 2.2.* --runtimeVersion 2.2.* "

  # Stable 3.0
  # "--description Baseline30 --aspnetCoreVersion 3.0 --runtimeVersion 3.0 "

  # Stable 3.1
  # RefPacks are broken for System.IO.Pipelining in 3.1.2, skip this version
  "--description Baseline31 --aspnetCoreVersion 3.1.1 --runtimeVersion 3.1.1 --sdk 3.1.101"
    
  # Current dev, running close to other baselines, with same repeat parameters
  "--description Baseline --runtimeversion 5.0.* 

)

jobs=(
  # Platform
  "-n PlaintextPlatform --webHost KestrelSockets $plaintextPlatformJobs"
  "-n JsonPlatform --webHost KestrelSockets $jsonPlatformJobs"
  "-n FortunesPlatform $htmlPlatformJobs --database PostgreSql"

  # Plaintext
  "-n Plaintext --webHost KestrelSockets $plaintextJobs"
  "-n PlaintextNonPipelined --webHost KestrelSockets $plaintextJobs"
  "-n MvcPlaintext --webHost KestrelSockets $plaintextJobs"
  "-n EndpointPlaintext --webHost KestrelSockets $plaintextJobs"
  "-n MvcJson --webHost KestrelSockets $jsonJobs"
  
  # JSon
  "-n Json --webHost KestrelSockets $jsonJobs"
  
  # Https
  "-n Plaintext -m https --webHost KestrelSockets $plaintextJobs"
  "-n Json -m https --webHost KestrelSockets $jsonJobs"
  "-n PlaintextNonPipelined -m https --webHost KestrelSockets $plaintextJobs"
  
  # Database Fortunes
  "-n DbFortunesRaw --webHost KestrelSockets $htmlJobs --database PostgreSql"
  "-n DbFortunesEf --webHost KestrelSockets $htmlJobs --database PostgreSql"
)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        for baseline in "${baselines[@]}"
        do
            echo "New job  on '$s': $job $baseline"
            dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job $baseline -i 1 --duration 20 --warmup 5 --quiet --session $SESSION -q "$BENCHMARKS_SQL" --table AspNetBaselines $BENCHMARKS_ARGS --self-contained
            # error code in $?
        done
    done
done
