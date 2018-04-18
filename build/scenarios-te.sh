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

common="--description TechEmpower --webHost Docker -t TechEmpower"
actixJobs="$common -j $ROOT/src/Benchmarks/benchmarks.te.actix.json"
aspnetcoreJobs="$common -j $ROOT/src/Benchmarks/benchmarks.te.aspnetcore.json"
aspnetcore21Jobs="$common -j $ROOT/src/Benchmarks/benchmarks.te.aspnetcore21.json"
nettyJobs="$common -j $ROOT/src/Benchmarks/benchmarks.te.netty.json"
nodejsJobs="$common -j $ROOT/src/Benchmarks/benchmarks.te.nodejs.json"
undertowJobs="$common -j $ROOT/src/Benchmarks/benchmarks.te.undertow.json"

jobs=(

  # Undertow
  "-n Plaintext-Undertow $undertowJobs"
  "-n PlaintextNonPipelined-Undertow $undertowJobs"
  "-n Json-Undertow $undertowJobs"
  "-n FortunesPostgreSql-Undertow $undertowJobs"

  # NodeJs
  "-n Plaintext-NodeJs $nodejsJobs"
  "-n PlaintextNonPipelined-NodeJs $nodejsJobs"
  "-n Json-NodeJs $nodejsJobs"
  "-n FortunesPostgreSql-NodeJs $nodejsJobs"

  # AspNetCore
  "-n Plaintext-AspNetCore $aspnetcoreJobs"
  "-n PlaintextNonPipelined-AspNetCore $aspnetcoreJobs"
  "-n Json-AspNetCore $aspnetcoreJobs"
  "-n FortunesPostgreSql-AspNetCore $aspnetcoreJobs"

    # AspNetCore21
  "-n Plaintext-AspNetCore21 $aspnetcore21Jobs"
  "-n PlaintextNonPipelined-AspNetCore21 $aspnetcore21Jobs"
  "-n Json-AspNetCore21 $aspnetcore21Jobs"
  "-n FortunesPostgreSql-AspNetCore21 $aspnetcore21Jobs"

  # Actix
  "-n Plaintext-Actix $actixJobs"
  "-n PlaintextNonPipelined-Actix $actixJobs"
  "-n Json-Actix $actixJobs"
  "-n FortunesPostgreSql-Actix $actixJobs"

    # Netty
  "-n Plaintext-Netty $nettyJobs"
  "-n PlaintextNonPipelined-Netty $nettyJobs"
  "-n Json-Netty $nettyJobs"
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
