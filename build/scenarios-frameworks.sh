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

trend="--description Trend/Latest"

jobs=(
  # NodeJS (JavaScript)

  "-j $ROOT/src/Benchmarks/benchmarks.te.nodejs.json $trend -n Plaintext-NodeJs --connections 4096 --no-clean --linux-only"
  "-j $ROOT/src/Benchmarks/benchmarks.te.nodejs.json $trend -n Json-NodeJs --connections 256 --no-clean --linux-only"
  "-j $ROOT/src/Benchmarks/benchmarks.te.nodejs.json $trend -n FortunesPostgreSql-NodeJs --connections 64 --no-clean --linux-only"

  # Actix (Rust)

  "-j $ROOT/src/Benchmarks/benchmarks.te.actix.json $trend -n Plaintext-Actix --connections 256 --no-clean --linux-only"
  "-j $ROOT/src/Benchmarks/benchmarks.te.actix.json $trend -n Json-Actix --connections 512 --no-clean --linux-only"
  "-j $ROOT/src/Benchmarks/benchmarks.te.actix.json $trend -n FortunesPostgreSql-Actix --connections 512 --no-clean --linux-only"

  # FastHttp (Go)

  "-j $ROOT/src/Benchmarks/benchmarks.te.fasthttp.json $trend -n Plaintext-FastHttp --connections 512 --no-clean --linux-only"
  "-j $ROOT/src/Benchmarks/benchmarks.te.fasthttp.json $trend -n Json-FastHttp --connections 512 --no-clean --linux-only"
  "-j $ROOT/src/Benchmarks/benchmarks.te.fasthttp.json $trend -n FortunesPostgreSql-FastHttp --connections 512 --no-clean --linux-only"

)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS --sdk latest --self-contained --collect-counters
        # error code in $?
    done
done
