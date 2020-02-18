package main

import (
	"flag"
	"fmt"
	"log"
	"net"
	"os"
	"os/signal"

	benchmark "github.com/grpc/grpc-dotnet/benchmark"
)

var tls = flag.Bool("tls", true, "Enable TLS on server")

func main() {
	lis, err := net.Listen("tcp", ":5000")
	if err != nil {
		log.Fatalf("Failed to listen: %v", err)
	}

	fmt.Println("Using TLS:", *tls)

	stop := benchmark.StartServer(benchmark.ServerInfo{Listener: lis, TLS: *tls})

	fmt.Println("Application started.")

	ch := make(chan os.Signal, 1)
	signal.Notify(ch, os.Interrupt)
	<-ch

	fmt.Println("Application stopping.")
	stop()
}
