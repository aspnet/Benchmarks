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

baselines=(
  # Latest (TC=ON; R2R=ON; Standalone=OFF)
  "--description TC1RR1SA0 --aspnetCoreVersion Latest --runtimeVersion Latest --env COMPlus_TieredCompilation=1 --env COMPlus_ReadyToRun=1"

  # Latest (TC=ON; R2R=ON; Standalone=ON)
  "--description TC1RR1SA1 --aspnetCoreVersion Latest --runtimeVersion Latest --env COMPlus_TieredCompilation=1 --env COMPlus_ReadyToRun=1 --self-contained"

  # Latest (TC=ON; R2R=OFF; Standalone=OFF)
  "--description TC1RR0SA0 --aspnetCoreVersion Latest --runtimeVersion Latest --env COMPlus_TieredCompilation=1 --env COMPlus_ReadyToRun=0"

  # Latest (TC=OFF; R2R=OFF; Standalone=OFF)
  "--description TC0RR0SA0 --aspnetCoreVersion Latest --runtimeVersion Latest --env COMPlus_TieredCompilation=0 --env COMPlus_ReadyToRun=0"

  # Latest (TC=OFF; R2R=ON; Standalone=OFF)
  "--description TC0RR1SA0 --aspnetCoreVersion Latest --runtimeVersion Latest --env COMPlus_TieredCompilation=0 --env COMPlus_ReadyToRun=1"
)

jobs=(
  # Plaintext
  "-n Plaintext --webHost KestrelSockets $plaintextJobs"
  "-n PlaintextNonPipelined --webHost KestrelSockets $plaintextJobs"
  "-n MvcPlaintext --webHost KestrelSockets $plaintextJobs"
  "-n Json --webHost KestrelSockets $jsonJobs"

  # Https
  "-n Plaintext -m https --webHost KestrelSockets $plaintextJobs"
  "-n PlaintextNonPipelined -m https --webHost KestrelSockets $plaintextJobs"
  "-n Json -m https --webHost KestrelSockets $jsonJobs"
  
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
            dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job $baseline -i 1 --duration 20 --warmup 20 --quiet --session $SESSION -q "$BENCHMARKS_SQL" --table AspNetTieredComp $BENCHMARKS_ARGS --sdk 3.0.100-rc2-014256
            # error code in $?
        done
    done
done
