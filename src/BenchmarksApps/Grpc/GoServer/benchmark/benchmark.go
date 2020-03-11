/*
 *
 * Copyright 2014 gRPC authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

//go:generate protoc -I grpc_testing --go_out=plugins=grpc:grpc_testing grpc_testing/control.proto grpc_testing/messages.proto grpc_testing/payloads.proto grpc_testing/services.proto grpc_testing/stats.proto

/*
Package benchmark implements the building blocks to setup end-to-end gRPC benchmarks.
*/
package benchmark

import (
	"context"
	"io"
	"log"
	"net"

	testpb "github.com/grpc/grpc-dotnet/grpc_testing"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/grpclog"
)

// Allows reuse of the same testpb.Payload object.
func setPayload(p *testpb.Payload, t testpb.PayloadType, size int) {
	if size < 0 {
		grpclog.Fatalf("Requested a response with invalid length %d", size)
	}
	body := make([]byte, size)
	switch t {
	case testpb.PayloadType_COMPRESSABLE:
	default:
		grpclog.Fatalf("Unsupported payload type: %d", t)
	}
	p.Type = t
	p.Body = body
}

// NewPayload creates a payload with the given type and size.
func NewPayload(t testpb.PayloadType, size int) *testpb.Payload {
	p := new(testpb.Payload)
	setPayload(p, t, size)
	return p
}

type testServer struct {
}

func (s *testServer) UnaryCall(ctx context.Context, in *testpb.SimpleRequest) (*testpb.SimpleResponse, error) {
	return &testpb.SimpleResponse{
		Payload: NewPayload(in.ResponseType, int(in.ResponseSize)),
	}, nil
}

func (s *testServer) StreamingCall(stream testpb.BenchmarkService_StreamingCallServer) error {
	in := new(testpb.SimpleRequest)
	for {
		// use ServerStream directly to reuse the same testpb.SimpleRequest object
		err := stream.(grpc.ServerStream).RecvMsg(in)
		if err == io.EOF {
			// read done.
			return nil
		}
		if err != nil {
			return err
		}

		response := &testpb.SimpleResponse{
			Payload: NewPayload(in.ResponseType, int(in.ResponseSize)),
		}
		if err := stream.Send(response); err != nil {
			return err
		}
	}
}

func (s *testServer) StreamingFromClient(stream testpb.BenchmarkService_StreamingFromClientServer) error {
	// Not implemented
	return nil
}

func (s *testServer) StreamingFromServer(in *testpb.SimpleRequest, stream testpb.BenchmarkService_StreamingFromServerServer) error {
	response := &testpb.SimpleResponse{
		Payload: NewPayload(in.ResponseType, int(in.ResponseSize)),
	}

	for {
		err := stream.Send(response)
		if err == io.EOF {
			// read done.
			return nil
		}
		if err != nil {
			return err
		}
	}
}

func (s *testServer) StreamingBothWays(stream testpb.BenchmarkService_StreamingBothWaysServer) error {
	// Not implemented
	return nil
}

// ServerInfo contains the information to create a gRPC benchmark server.
type ServerInfo struct {
	// Metadata is an optional configuration.
	// For "protobuf", it's ignored.
	// For "bytebuf", it should be an int representing response size.
	Metadata interface{}

	// Flag indicating whether to use TLS
	TLS bool

	// Listener is the network listener for the server to use
	Listener net.Listener
}

// StartServer starts a gRPC server serving a benchmark service according to info.
// It returns a function to stop the server.
func StartServer(info ServerInfo, opts ...grpc.ServerOption) func() {
	opts = append(opts, grpc.WriteBufferSize(128*1024))
	opts = append(opts, grpc.ReadBufferSize(128*1024))

	if info.TLS {
		// Use certs from gRPC testdata module
		creds, err := credentials.NewServerTLSFromFile("certs/server1.pem", "certs/server1.key")
		if err != nil {
			log.Fatalf("failed to create credentials: %v", err)
		}

		opts = append(opts, grpc.Creds(creds))
	}

	s := grpc.NewServer(opts...)
	testpb.RegisterBenchmarkServiceServer(s, &testServer{})
	go s.Serve(info.Listener)
	return func() {
		s.Stop()
	}
}
