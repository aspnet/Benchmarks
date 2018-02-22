@echo off

: Traces collected by container will be written to the current directory
docker run -it --rm -v %cd%:/traces benchmarks-driver %*
