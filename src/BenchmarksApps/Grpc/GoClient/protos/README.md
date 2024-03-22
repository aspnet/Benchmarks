Command to generate code from `.proto` files:

```
protoc --go_out=. --go_opt=paths=source_relative --go-grpc_out=. --go-grpc_opt=paths=source_relative benchmark_service.proto grpc/testing/messages.proto
```
