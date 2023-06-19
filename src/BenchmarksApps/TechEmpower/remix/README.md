# Fortunes implementation using Remix

This is an implementation of the [TechEmpower Fortunes benchmark](https://github.com/TechEmpower/FrameworkBenchmarks/wiki/Project-Information-Framework-Tests-Overview#fortunes) using Remix

Run the app by executing `npm run build && npm start` in the app root.

The app requires a Postgres database based on the [TechEmpower Postgres Docker image](https://github.com/TechEmpower/FrameworkBenchmarks/tree/master/toolset/databases/postgres). Clone the [TechEmpower repo](https://github.com/TechEmpower/FrameworkBenchmarks), navigate to `./toolset/databases/postgres`, and run `docker build -f .\postgres.dockerfile -t postgres_te .` to build a container from that image, then `docker run -p 5432:5432 --name postgres_te postgres_te`.

The [Dockerfile](./Dockerfile) will build a standalone image (based on node-alpine) for running the app on port 3000. Note that in docker the host name for the Postgres database is set to `postgres_te`.
