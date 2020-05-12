package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"log"
	"math"
	"os"
	"strings"
	"sync"
	"time"

	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"

	testpb "github.com/grpc/grpc-dotnet/grpc_testing"
)

var (
	protocol              = flag.String("protocol", "h2c", "Connection protocol. h2 uses TLS, else plain TCP")
	caFile                = flag.String("ca_file", "", "The file containing the CA root cert file")
	serverAddr            = flag.String("server_addr", "localhost:5000", "The server address in the format of host:port")
	serverHostOverride    = flag.String("server_host_override", "x.test.youtube.com", "The server name used to verify the hostname returned by the TLS handshake")
	scenario              = flag.String("scenario", "unary", "The scenario to run")
	requestSize           = flag.Int("request_size", 0, "Request payload size")
	responseSize          = flag.Int("response_size", 0, "Response payload size")
	connectionCount       = flag.Int("connections", 1, "HTTP/2 connections")
	streams               = flag.Int("streams", 1, "Streams per connection")
	warmup                = flag.Int("warmup", 5, "Warmup in seconds")
	duration              = flag.Int("duration", 10, "Duration in seconds")
	wg                    sync.WaitGroup
	connections           []*grpc.ClientConn
	connectionLocks       []sync.Mutex
	requestsPerConnection []int
	failuresPerConnection []int
	latencyPerConnection  []latency
	stopped               bool
	warmingUp             bool
)

type latency struct {
	count int
	sum   float64
}

type jobMetadata struct {
	Source           string
	Name             string
	ShortDescription string
	LongDescription  string
}

type jobMeasurement struct {
	Timestamp time.Time
	Name      string
	Value     float64
}

type jobStatistics struct {
	Metadata     []jobMetadata
	Measurements []jobMeasurement
}

func main() {
	fmt.Println("gRPC Client")
	fmt.Println("Args:", os.Args)

	flag.Parse()

	var opts []grpc.DialOption
	if *protocol == "h2" {
		if *caFile == "" {
			log.Fatal("ca_file required with TLS")
			return
		}
		creds, err := credentials.NewClientTLSFromFile(*caFile, *serverHostOverride)
		if err != nil {
			log.Fatalf("Failed to create TLS credentials %v", err)
		}
		opts = append(opts, grpc.WithTransportCredentials(creds))
	} else {
		opts = append(opts, grpc.WithInsecure())
	}

	opts = append(opts, grpc.WithBlock())

	buildConnections(context.Background(), opts)

	go func() {
		warmingUp = true
		time.Sleep(time.Duration(*warmup) * time.Second)
		warmingUp = false
		fmt.Print("Finished warming up")
		time.Sleep(time.Duration(*duration) * time.Second)
		stopped = true
	}()

	for connectionID, cc := range connections {
		runWithConn(connectionID, cc)
	}
	wg.Wait()

	calculateRequestStatistics()
	calculateLatencyStatistics()
}

func calculateRequestStatistics() {
	totalRequests := 0
	totalFailures := 0
	min := math.MaxInt32
	max := 0
	for connectionID := range connections {
		connectionRequests := requestsPerConnection[connectionID]

		totalRequests += connectionRequests
		totalFailures += failuresPerConnection[connectionID]

		if connectionRequests > max {
			max = connectionRequests
		}
		if connectionRequests < min {
			min = connectionRequests
		}
	}

	rps := totalRequests / int(*duration)
	fmt.Printf("Least Requests per Connection: %d", min)
	fmt.Printf("Most Requests per Connection: %d", max)
	fmt.Printf("RPS %d", rps)

	metadata := []jobMetadata{
		jobMetadata{
			Source:           "Benchmarks",
			Name:             "grpc/rps/max",
			ShortDescription: "Max RPS",
			LongDescription:  "RPS: max",
		},
		jobMetadata{
			Source:           "Benchmarks",
			Name:             "grpc/requests",
			ShortDescription: "Requests",
			LongDescription:  "Total number of requests",
		},
		jobMetadata{
			Source:           "Benchmarks",
			Name:             "grpc/errors/badresponses",
			ShortDescription: "Bad responses",
			LongDescription:  "Non-2xx or 3xx responses",
		},
	}
	measurements := []jobMeasurement{
		jobMeasurement{
			Name:      "grpc/rps/max",
			Timestamp: time.Now().UTC(),
			Value:     float64(rps),
		},
		jobMeasurement{
			Name:      "grpc/requests",
			Timestamp: time.Now().UTC(),
			Value:     float64(totalRequests),
		},
		jobMeasurement{
			Name:      "grpc/errors/badresponses",
			Timestamp: time.Now().UTC(),
			Value:     float64(totalFailures),
		},
	}

	writeJobStatistics(metadata, measurements)
}

func calculateLatencyStatistics() {
	totalRequests := 0
	totalSum := float64(0)
	for connectionID := range connections {
		latency := latencyPerConnection[connectionID]
		totalRequests += latency.count
		totalSum += latency.sum
	}
	if totalRequests > 0 {
		totalSum = totalSum / float64(totalRequests)
	}

	fmt.Printf("Average latency %fms", totalSum)

	metadata := []jobMetadata{
		jobMetadata{
			Source:           "Benchmarks",
			Name:             "grpc/latency/mean",
			ShortDescription: "Mean latency (ms)",
			LongDescription:  "Mean latency (ms)",
		},
	}
	measurements := []jobMeasurement{
		jobMeasurement{
			Name:      "grpc/latency/mean",
			Timestamp: time.Now().UTC(),
			Value:     totalSum,
		},
	}

	writeJobStatistics(metadata, measurements)
}

func runWithConn(connectionID int, cc *grpc.ClientConn) {
	for i := 0; i < *streams; i++ {
		streamID := i
		wg.Add(1)
		go func() {
			defer wg.Done()
			caller := makeCaller(cc, connectionID, streamID)
			log.Printf("Starting %d %d", connectionID, streamID)
			caller()
			log.Printf("Finished %d %d", connectionID, streamID)
		}()
	}
}

func makeCaller(cc *grpc.ClientConn, connectionID int, streamID int) func() {
	client := testpb.NewBenchmarkServiceClient(cc)
	if *scenario != "unary" {
		log.Fatalf("Unsupported scenario: %s", *scenario)
	}
	return func() {
		for {
			request := &testpb.SimpleRequest{
				Payload:      NewPayload(int(*requestSize)),
				ResponseSize: int32(*responseSize),
			}

			start := time.Now()
			_, err := client.UnaryCall(context.Background(), request)
			end := time.Now()

			if err != nil {
				handleFailure(connectionID)
			} else {
				handleSuccess(connectionID, start, end)
			}

			if stopped {
				return
			}
		}
	}
}

func handleFailure(connectionID int) {
	if stopped || warmingUp {
		return
	}
	connectionLocks[connectionID].Lock()
	failuresPerConnection[connectionID]++
	connectionLocks[connectionID].Unlock()
}

func handleSuccess(connectionID int, start time.Time, end time.Time) {
	if stopped || warmingUp {
		return
	}
	connectionLocks[connectionID].Lock()
	requestsPerConnection[connectionID]++

	count := latencyPerConnection[connectionID].count
	sum := latencyPerConnection[connectionID].sum

	callLatency := end.Sub(start)

	count++
	sum += float64(callLatency.Nanoseconds()) / float64(1000*1000)

	latencyPerConnection[connectionID] = latency{
		count: count,
		sum:   sum,
	}
	connectionLocks[connectionID].Unlock()
}

func buildConnections(ctx context.Context, opts []grpc.DialOption) {
	// remove http:// or https:// from server address
	resolvedAddr := *serverAddr
	resolvedAddr = strings.TrimPrefix(resolvedAddr, "http://")
	resolvedAddr = strings.TrimPrefix(resolvedAddr, "https://")

	fmt.Printf("Building connections to %s", resolvedAddr)

	connections = make([]*grpc.ClientConn, *connectionCount)
	connectionLocks = make([]sync.Mutex, *connectionCount)
	requestsPerConnection = make([]int, *connectionCount)
	failuresPerConnection = make([]int, *connectionCount)
	latencyPerConnection = make([]latency, *connectionCount)

	for i := range connections {
		conn, err := grpc.Dial(resolvedAddr, opts...)
		if err != nil {
			log.Fatalf("fail to dial: %v", err)
		}
		connections[i] = conn
	}
}

func setPayload(p *testpb.Payload, size int) {
	if size < 0 {
		log.Fatalf("Requested an invalid length %d", size)
	}
	body := make([]byte, size)
	p.Body = body
}

// NewPayload creates a payload with the given type and size.
func NewPayload(size int) *testpb.Payload {
	p := new(testpb.Payload)
	setPayload(p, size)
	return p
}

func writeJobStatistics(metadata []jobMetadata, measurements []jobMeasurement) {
	statistics := jobStatistics{
		metadata,
		measurements,
	}

	json, err := json.Marshal(statistics)
	if err != nil {
		log.Fatalf("Failed to create JSON %v", err)
	}

	fmt.Printf("#StartJobStatistics")
	fmt.Println(string(json))
	fmt.Printf("#EndJobStatistics")
}
