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

# compute current directory
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT=$DIR/..

defaultArgs = "--description TechEmpower --webHost Docker"
actixJobs="$defaultArgs -j $ROOT/src/Benchmarks/benchmarks.te.actix.json"
aspnetcoreJobs="$defaultArgs -j $ROOT/src/Benchmarks/benchmarks.te.aspnetcore.json"
nodejsJobs="$defaultArgs -j $ROOT/src/Benchmarks/benchmarks.te.nodejs.json"
undertowJobs="$defaultArgs -j $ROOT/src/Benchmarks/benchmarks.te.undertow.json"


jobs=(

  # Undertow
  "-n Plaintext-Actix $undertowJobs"
  "-n PlaintextNonPipelined-Actix $undertowJobs"
  "-n Json-Actix $undertowJobs"
  "-n FortunesPostgreSql-Actix $undertowJobs"

  # NodeJs
  "-n Plaintext-Actix $nodejsJobs"
  "-n PlaintextNonPipelined-Actix $nodejsJobs"
  "-n Json-Actix $nodejsJobs"
  "-n FortunesPostgreSql-Actix $nodejsJobs"

  # AspNetCore
  "-n Plaintext-Actix $aspnetcoreJobs"
  "-n PlaintextNonPipelined-Actix $aspnetcoreJobs"
  "-n Json-Actix $aspnetcoreJobs"
  "-n FortunesPostgreSql-Actix $aspnetcoreJobs"

  # Actix
  "-n Plaintext-Actix $actixJobs"
  "-n PlaintextNonPipelined-Actix $actixJobs"
  "-n Json-Actix $actixJobs"
  "-n FortunesPostgreSql-Actix $actixJobs"
)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS
        # error code in $?
    done
done
