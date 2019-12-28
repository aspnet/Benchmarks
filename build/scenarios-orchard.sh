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

orchardJobs="-j $ROOT/src/Benchmarks/benchmarks.orchard.json"

trend="--description Trend/Latest"

jobs=(

  # Orchard
  "-n OrchardBlog $trend $orchardJobs --output-archive https://raw.githubusercontent.com/aspnet/Benchmarks/master/resources/Orchard/App_Data_Blog.zip;App_Data"
  "-n OrchardBlogPost $trend $orchardJobs --output-archive https://raw.githubusercontent.com/aspnet/Benchmarks/master/resources/Orchard/App_Data_Blog.zip;App_Data"
  "-n OrchardBlogAbout $trend $orchardJobs --output-archive https://raw.githubusercontent.com/aspnet/Benchmarks/master/resources/Orchard/App_Data_Blog.zip;App_Data"

)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS --sdk 3.1.100  --aspnetcoreversion 3.1 --runtimeversion 3.1 --collect-counters
        # error code in $?
    done
done
