# Benchmarks\Docker

## BenchmarksClient (Load Generator)
```
git clone https://github.com/aspnet/benchmarks
cd benchmarks/docker/benchmarks
./build.sh
./run-client.sh
```

## BenchmarksServer (Web Server)
```
git clone https://github.com/aspnet/benchmarks
cd benchmarks/docker/benchmarks
./build.sh
./run-server.sh
```

## PostgreSQL configured for TechEmpower
```
git clone https://github.com/aspnet/benchmarks
cd benchmarks/docker/postgres-techempower
./build.sh
./run.sh
```

## PostgreSQL Fortunes Benchmark
```
git clone https://github.com/aspnet/benchmarks
cd benchmarks/docker/postgres-techempower-fortunesbench
./build.sh
./run.sh server-name-or-ip
```