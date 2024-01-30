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

	testpb "github.com/grpc/grpc-dotnet/protos"
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
	ctx, cancel           = context.WithCancel(context.Background())
	warmupWg              sync.WaitGroup
	finishedWg            sync.WaitGroup
	connections           []*grpc.ClientConn
	connectionLocks       []sync.Mutex
	requestsPerConnection []int
	failuresPerConnection []int
	latencyPerConnection  []latency
	maxLatency            float64
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
	Reduce           string
	Aggregate        string
	ShortDescription string
	LongDescription  string
	Format           string
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

	// Create connections and related collections
	buildConnections(context.Background(), opts)

	// Start background thread to track warmup and duration
	go func() {
		warmup := time.Duration(*warmup) * time.Second
		fmt.Printf("Warming up for %v\n", warmup)
		warmingUp = true
		warmupWg.Add(1)
		time.Sleep(warmup)

		fmt.Print("Finished warming up\n")
		warmingUp = false
		warmupWg.Done()

		duration := time.Duration(*duration) * time.Second
		fmt.Printf("Running for %v\n", duration)
		time.Sleep(duration)

		fmt.Print("Stopping benchmarks\n")
		stopped = true
		cancel()
	}()

	// Start caller threads for each connection + stream
	for connectionID, cc := range connections {
		runWithConn(connectionID, cc)
	}
	// Wait for caller threads to finish

	fmt.Print("Waiting for caller threads to finish\n")
	finishedWg.Wait()

	fmt.Print("Caller threads finished\n")

	// Output results
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
	fmt.Printf("Least Requests per Connection: %d\n", min)
	fmt.Printf("Most Requests per Connection: %d\n", max)
	fmt.Printf("Total failures: %d\n", totalFailures)
	fmt.Printf("RPS: %d\n", rps)

	metadata := []jobMetadata{
		{
			Source:           "Benchmarks",
			Name:             "grpc/rps/max;http/rps/mean",
			Reduce:           "Sum",
			Aggregate:        "Max",
			ShortDescription: "Requests/sec",
			LongDescription:  "Requests per second",
			Format:           "n0",
		},
		{
			Source:           "Benchmarks",
			Name:             "grpc/requests",
			Reduce:           "Sum",
			Aggregate:        "Max",
			ShortDescription: "Requests",
			LongDescription:  "Total number of requests",
			Format:           "n0",
		},
		{
			Source:           "Benchmarks",
			Name:             "grpc/errors/badresponses",
			Reduce:           "Sum",
			Aggregate:        "Max",
			ShortDescription: "Bad responses",
			LongDescription:  "Non-2xx or 3xx responses",
			Format:           "n0",
		},
	}
	measurements := []jobMeasurement{
		{
			Name:      "grpc/rps/max;http/rps/mean",
			Timestamp: time.Now().UTC(),
			Value:     float64(rps),
		},
		{
			Name:      "grpc/requests",
			Timestamp: time.Now().UTC(),
			Value:     float64(totalRequests),
		},
		{
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

	fmt.Printf("Average latency: %fms\n", totalSum)
	fmt.Printf("Max latency: %fms\n", maxLatency)

	metadata := []jobMetadata{
		{
			Source:           "Benchmarks",
			Name:             "grpc/latency/mean;http/latency/mean",
			Reduce:           "Sum",
			Aggregate:        "Max",
			ShortDescription: "Mean latency (ms)",
			LongDescription:  "Mean latency (ms)",
			Format:           "n0",
		},
		{
			Source:           "Benchmarks",
			Name:             "grpc/latency/max;http/latency/max",
			Reduce:           "Sum",
			Aggregate:        "Max",
			ShortDescription: "Max latency (ms)",
			LongDescription:  "Max latency (ms)",
			Format:           "n0",
		},
	}
	measurements := []jobMeasurement{
		{
			Name:      "grpc/latency/mean;http/latency/mean",
			Timestamp: time.Now().UTC(),
			Value:     totalSum,
		},
		{
			Name:      "grpc/latency/max;http/latency/max",
			Timestamp: time.Now().UTC(),
			Value:     maxLatency,
		},
	}

	writeJobStatistics(metadata, measurements)
}

func runWithConn(connectionID int, cc *grpc.ClientConn) {
	for i := 0; i < *streams; i++ {
		streamID := i
		finishedWg.Add(1)
		go func() {
			defer finishedWg.Done()
			caller := makeCaller(cc, connectionID, streamID, *scenario)
			if caller == nil {
				log.Fatalf("Unsupported scenario: %s", *scenario)
			}
			fmt.Printf("Starting %d %d\n", connectionID, streamID)
			caller()
			fmt.Printf("Finished %d %d\n", connectionID, streamID)
		}()
	}
}

func makeCaller(cc *grpc.ClientConn, connectionID int, streamID int, scenario string) func() {
	client := testpb.NewBenchmarkServiceClient(cc)
	if scenario == "unary" {
		return func() {
			for {
				request := &testpb.SimpleRequest{
					Payload:      NewPayload(int(*requestSize)),
					ResponseSize: int32(*responseSize),
				}

				start := time.Now()
				if _, err := client.UnaryCall(ctx, request); err != nil {
					end := time.Now()
					handleRequest(connectionID, start, end)
					handleFailure(connectionID, err)
				} else {
					end := time.Now()
					handleRequest(connectionID, start, end)
				}

				if stopped {
					return
				}
			}
		}
	}
	if scenario == "serverstreaming" {
		return func() {
			request := &testpb.SimpleRequest{
				Payload:      NewPayload(int(*requestSize)),
				ResponseSize: int32(*responseSize),
			}

			stream, err := client.StreamingFromServer(ctx, request)
			if err != nil {
				// Wait for warmup to be finished before reporting the call failed
				warmupWg.Wait()
				handleFailure(connectionID, err)
				return
			}

			for {
				start := time.Now()
				if _, err := stream.Recv(); err != nil {
					end := time.Now()
					handleRequest(connectionID, start, end)
					handleFailure(connectionID, err)
				} else {
					end := time.Now()
					handleRequest(connectionID, start, end)
				}

				if stopped {
					return
				}
			}
		}
	}
	if scenario == "pingpongstreaming" {
		return func() {
			stream, err := client.StreamingCall(ctx)
			if err != nil {
				// Wait for warmup to be finished before reporting the call failed
				warmupWg.Wait()
				handleFailure(connectionID, err)
				return
			}

			for {
				request := &testpb.SimpleRequest{
					Payload:      NewPayload(int(*requestSize)),
					ResponseSize: int32(*responseSize),
				}

				start := time.Now()
				if err := stream.Send(request); err != nil {
					end := time.Now()
					handleRequest(connectionID, start, end)
					handleFailure(connectionID, err)
				} else {
					if _, err := stream.Recv(); err != nil {
						end := time.Now()
						handleRequest(connectionID, start, end)
						handleFailure(connectionID, err)
					} else {
						end := time.Now()
						handleRequest(connectionID, start, end)
					}
				}

				if stopped {
					return
				}
			}
		}
	}

	return nil
}

func handleFailure(connectionID int, err error) {
	if warmingUp {
		return
	}
	if stopped {
		fmt.Printf("Failure after stop: %v\n", err)
		return
	}
	connectionLocks[connectionID].Lock()
	failuresPerConnection[connectionID]++
	connectionLocks[connectionID].Unlock()
}

func handleRequest(connectionID int, start time.Time, end time.Time) {
	if stopped || warmingUp {
		return
	}
	connectionLocks[connectionID].Lock()
	requestsPerConnection[connectionID]++

	count := latencyPerConnection[connectionID].count
	sum := latencyPerConnection[connectionID].sum

	callLatency := end.Sub(start)
	callLatencyMs := float64(callLatency.Nanoseconds()) / float64(1000*1000)

	count++
	sum += callLatencyMs

	maxLatency = math.Max(callLatencyMs, maxLatency)

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

	fmt.Printf("Building connections to %s\n", resolvedAddr)

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

	// Need to write statistics JSON together with start and end markers to ensure
	// that no other console content is mixed together with the JSON.
	fmt.Println("#StartJobStatistics\n" + string(json) + "\n#EndJobStatistics")
}
