# Crank Agent image for TLS tests

...

### Crontab configuration

Machines are configured to perform a git pull, get latest changes, rebuild crank and restart it once in a while. On linux machines it is happening via `cron`.
In order to be able to use Dockerfile from this folder (`/crank/agent`), one should change the crontab.

To lookup crontab configured:
```
sudo crontab -l
```

You can use crontab like this:
```
0 0 * * * cd /home/dotnetperfuser/src/crank/docker/agent; ./stop.sh; docker rm -f $(docker ps -a -q --filter "label=benchmarks"); docker system prune --all --force --volumes; git checkout -f main; git reset --hard; git pull; cd /home/dotnetperfuser/src/Benchmarks; git checkout -f main; git reset --hard; git pull; cp -rf /home/dotnetperfuser/src/Benchmarks/src/BenchmarksApps/TLS/crank/agent/* /home/dotnetperfuser/src/crank/docker/agent/; cd /home/dotnetperfuser/src/crank/docker/agent; ./build.sh <arguments>; ./run.sh <arguments>
```

Cron tab does the following:
1) fetches the latest dotnet/crank
2) fetches latest aspnetcore/Benchmarks
3) copies dockerfile from aspnetcore/Benchmarks into dotnet/crank
4) builds and runs the crank agent using custom Dockerfile

