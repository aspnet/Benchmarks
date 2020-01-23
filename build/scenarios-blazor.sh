if [ -z "$BENCHMARKS_SERVER" ]
then
    echo "\$BENCHMARKS_SERVER is not set"
    exit 1
fi

# compute current directory
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT=$DIR/..
SESSION=`date '+%Y%m%d%H%M%S'`

blazorJobs="--config https://raw.githubusercontent.com/dotnet/aspnetcore/blazor-wasm/src/Components/benchmarkapps/Wasm.Performance/benchmarks.compose.json "

jobs=(
  # Blazor
  "--scenario blazorwasmbenchmark $blazorJobs --application.options.requiredOperatingSystem Linux"
)

# build driver
cd $ROOT/src/BenchmarksDriver2
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver2

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver2/BenchmarksDriver.dll --application.endpoints $s $job --session $SESSION --sql "$BENCHMARKS_SQL" --table BlazorWasm $BENCHMARKS_ARGS

        # error code in $?
    done
done
