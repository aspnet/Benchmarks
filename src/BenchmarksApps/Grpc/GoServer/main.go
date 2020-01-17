package main

import (
	"fmt"
	"log"
	"net"
	"os"
	"os/signal"

	benchmark "github.com/grpc/grpc-dotnet/benchmark"
)

func main() {
	lis, err := net.Listen("tcp", ":5000")
	if err != nil {
		log.Fatalf("Failed to listen:  %v", err)
	}

	stop := benchmark.StartServer(benchmark.ServerInfo{Type: "protobuf", Listener: lis})

	fmt.Println("Application started.")

	ch := make(chan os.Signal, 1)
	signal.Notify(ch, os.Interrupt)
	<-ch

	fmt.Println("Application stopping.")
	stop()
}
