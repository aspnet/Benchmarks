if [ -z "$BENCHMARKS_SERVER" ]
then
    echo "\$BENCHMARKS_SERVER is not set"
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

efcoreJobs="-r EntityFrameworkCore@release/3.1 --description Benchmarks --arg --perflab --no-global-json --server-timeout 00:45:00 -t EFCoreBenchmarks --aspnetcoreversion 3.1 --runtimeversion 3.1"

jobs=(

  "$efcoreJobs --benchmarkdotnet:*AddDataVariations* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*ExistingDataVariations* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*ChildVariations* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*Delete* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*ExistingDataVariations* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*FuncletizationSqliteTests* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*InitializationSqliteTests* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*Insert* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*Mixed* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*ParentVariations* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*RawSqlQuerySqliteTests* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*SimpleQuerySqliteTests* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"
  "$efcoreJobs --benchmarkdotnet:*Update* -n Sqlite --project-file benchmark/EFCore.Sqlite.Benchmarks/EFCore.Sqlite.Benchmarks.csproj"

)

# build driver
cd $ROOT/src/BenchmarksDriver
dotnet publish -c Release -o $ROOT/.build/BenchmarksDriver

for s in ${BENCHMARKS_SERVER//,/ }
do
    for job in "${jobs[@]}"
    do
        echo "New job  on '$s': $job"
        dotnet $ROOT/.build/BenchmarksDriver/BenchmarksDriver.dll -s $s -c $BENCHMARKS_CLIENT $job --session $SESSION -q "$BENCHMARKS_SQL" $BENCHMARKS_ARGS 
        # error code in $?
    done
done
