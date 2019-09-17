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

linkAThonJobs="-j $ROOT/src/Benchmarks/link-a-thon.api-template.json"

trend="--description Trend/Latest"

jobs=(
  # link-a-thon
  "-n LinkAThonBaseline $trend $linkAThonJobs --linux-only" 
  "-n LinkAThonTrimmedAndR2R $trend $linkAThonJobs --linux-only" 
  "-n LinkAThonTrimmedAndR2RSingleFile $trend $linkAThonJobs --linux-only"
  "-n LinkAThonTrimmedAndR2RSingleFileWithTrimList $trend $linkAThonJobs --linux-only"
  "-n LinkAThonTrimmedAndR2RSingleFileNoMvc $trend $linkAThonJobs --linux-only" 
  "-n LinkAThonTrimmedAndR2RSingleFileCustomHost $trend $linkAThonJobs --linux-only" 
)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS --sdk 3.0.100-rc2-014256 --collect-counters
        # error code in $?
    done
done
