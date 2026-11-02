# Benchmarks in Docker

## BenchmarksDriver (Coordinates Client and Server)
```
git clone https://github.com/aspnet/benchmarks
cd benchmarks/docker/benchmarks
./build.sh
./run-driver.sh -s http://server:5001 -c http://client:5002 -n scenario
```

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

## Service Bus controller

Run `docker/servicebus-controller/run.sh <servicename> [docker options]` to launch
the `azdocontroller` image. Arguments after the service name are Docker options,
not `crank-azdo` arguments.

Certificate authentication is the default when `SERVICE_BUS_MI_CLIENT_ID` is unset
or empty:

```bash
cd docker/servicebus-controller
export SERVICE_BUS_CONNECTION_STRING="<namespace>.servicebus.windows.net"
export SERVICE_BUS_QUEUE="<queue>"
export SERVICE_BUS_CLIENTID="<service-bus-application-client-id>"
export SERVICE_BUS_TENANTID="<tenant-id>"
export SERVICE_BUS_CERT_PATH="/path/to/servicebus.pfx"
export SQL_CONNECTION_STRING="<sql-connection-string>"
export SQL_SERVER_CLIENTID="<sql-application-client-id>"
export SQL_SERVER_TENANTID="<tenant-id>"
export SQL_SERVER_CERT_PATH="/path/to/sqlserver.pfx"
unset SERVICE_BUS_MI_CLIENT_ID
./run.sh controller --log-opt max-size=1G
```

To select managed identity for the worker's Service Bus connection, set
`SERVICE_BUS_MI_CLIENT_ID` to the client-ID GUID of the user-assigned managed
identity attached to the host. Change this value to select a different identity.
The identity must already have the required Service Bus permissions.

```bash
# Keep the queue, namespace, and SQL settings from the example above.
export SERVICE_BUS_MI_CLIENT_ID="<user-assigned-managed-identity-client-id>"
unset SERVICE_BUS_CLIENTID SERVICE_BUS_TENANTID SERVICE_BUS_CERT_PATH
./run.sh controller --log-opt max-size=1G
```

Despite its name, `SERVICE_BUS_CONNECTION_STRING` must contain the fully qualified
namespace hostname for certificate and MI authentication, not an `Endpoint=...`
connection string or an `sb://` URI. Use the appropriate hostname for your Azure
cloud.

A nonempty `SERVICE_BUS_MI_CLIENT_ID` takes precedence over Service Bus certificate
settings. The script explicitly passes the identity into Docker, overrides the
image's certificate entrypoint with `crank-azdo --mi-client-id`, and omits the
Service Bus certificate mount. Invalid MI configuration fails without falling back
to certificates. Setting `AZURE_CLIENT_ID` alone does not enable this mode.
The image's default entrypoint remains unchanged for direct Docker launches.

**SQL authentication is independent.** Current queued benchmark commands explicitly
request SQL certificate authentication, so both examples still require the SQL
settings and certificate file. The script mounts it at `/certs/sqlserver.pfx` and
sets the container's `SQL_SERVER_CERT_PATH` accordingly. Selecting MI for the
worker does not select SQL MI or add `--mi-client-id` to queued child commands.
SQL MI requires a separate queued-command change using the controller's existing
`--mi-client-id` option and may select a different identity. If that option
references an environment variable, pass the variable into Docker separately.

Run the launch-script checks with `bash docker/servicebus-controller/test-run.sh`
from the repository root. They use a Docker stub and do not contact Azure or start
containers.
