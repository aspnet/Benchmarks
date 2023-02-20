package main

import (
	"flag"
	"fmt"
	"log"
	"net"
	"os"
	"os/signal"

	"github.com/grpc/grpc-dotnet/server"
)

func main() {
	protocol := flag.String("protocol", "h2c", "Enable TLS on server")
	flag.Parse()

	fmt.Println("Args:", os.Args)

	tls := *protocol == "h2"
	fmt.Println("Using TLS:", *protocol)

	lis, err := net.Listen("tcp", ":5000")
	if err != nil {
		log.Fatalf("Failed to listen: %v", err)
	}

	stop := server.StartServer(server.ServerInfo{Listener: lis, TLS: tls})

	fmt.Println("Application started.")

	ch := make(chan os.Signal, 1)
	signal.Notify(ch, os.Interrupt)
	<-ch

	fmt.Println("Application stopping.")
	stop()
}
