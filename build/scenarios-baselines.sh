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

plaintextJobs="-j $ROOT/src/Benchmarks/benchmarks.plaintext.json"
htmlJobs="-j $ROOT/src/Benchmarks/benchmarks.html.json"
jsonJobs="-j $ROOT/src/Benchmarks/benchmarks.json.json"

baselines=(
  # Stable 2.1
  "--description Baseline21 --aspnetCoreVersion 2.1 --runtimeVersion 2.1" 
  
  # Servicing 2.1
  "--description Baseline21Servicing --aspnetCoreVersion 2.1.* --runtimeVersion 2.1.*"

  # Stable 2.2
  "--description Baseline22 --aspnetCoreVersion 2.2 --runtimeVersion 2.2"

  # Servicing 2.2
  "--description Baseline22Servicing --aspnetCoreVersion 2.2.* --runtimeVersion 2.2.*"

  # Current dev, running close to other baselines, with same repeat parameters
  "--description Baseline --aspnetCoreVersion Latest --runtimeVersion Latest"

  # Stable 2.2, Tiered Compilation on
  "--description Baseline22TC --aspnetCoreVersion 2.2 --runtimeVersion 2.2 --tiered-compilation"

)

jobs=(
  # Plaintext
  "-n Plaintext --webHost KestrelSockets $plaintextJobs"
  "-n PlaintextNonPipelined --webHost KestrelSockets $plaintextJobs"
  "-n MvcPlaintext --webHost KestrelSockets $plaintextJobs"
  "-n Json --webHost KestrelSockets $jsonJobs"
  "-n MvcJson --webHost KestrelSockets $jsonJobs"

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
            echo "New job  on '$s': $job"
            dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job $baseline -i 3 --duration 10 --warmup 3 --quiet -q "$BENCHMARKS_SQL" --table AspNetBaselines $BENCHMARKS_ARGS
            # error code in $?
        done
    done
done
