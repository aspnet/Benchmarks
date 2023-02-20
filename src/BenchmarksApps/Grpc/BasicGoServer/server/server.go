package server

import (
	"context"
	"log"
	"net"
	"time"

	pb "github.com/grpc/grpc-dotnet/protos"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"google.golang.org/protobuf/types/known/timestamppb"
)

var allTodos = []*pb.Todo{
	&pb.Todo{Id: 0, Title: "Wash the dishes.", DueBy: timestamppb.New(time.Now()), IsComplete: true},
	&pb.Todo{Id: 0, Title: "Dry the dishes.", DueBy: timestamppb.New(time.Now()), IsComplete: true},
	&pb.Todo{Id: 0, Title: "Turn the dishes over.", DueBy: timestamppb.New(time.Now()), IsComplete: true},
	&pb.Todo{Id: 0, Title: "Walk the kangaroo.", DueBy: timestamppb.New(time.Now().Add(24 * time.Hour)), IsComplete: true},
	&pb.Todo{Id: 0, Title: "Call Grandma.", DueBy: timestamppb.New(time.Now().Add(24 * time.Hour)), IsComplete: true},
}
var allTodosResponse = pb.GetAllTodosResponse{
	AllTodos: allTodos,
}

type testServer struct {
	pb.UnimplementedTodoServiceServer
}

func (s *testServer) GetTodo(ctx context.Context, in *pb.GetTodoRequest) (*pb.GetTodoResponse, error) {
	for _, s := range allTodos {
		if s.Id == in.Id {
			return &pb.GetTodoResponse{
				Todo: s,
			}, nil
		}
	}
	return &pb.GetTodoResponse{}, nil
}

func (s *testServer) GetAllTodos(ctx context.Context, in *pb.GetAllTodosRequest) (*pb.GetAllTodosResponse, error) {
	return &allTodosResponse, nil
}

type ServerInfo struct {
	// Flag indicating whether to use TLS
	TLS bool

	// Listener is the network listener for the server to use
	Listener net.Listener
}

func StartServer(info ServerInfo, opts ...grpc.ServerOption) func() {

	if info.TLS {
		// Use certs from gRPC testdata module
		creds, err := credentials.NewServerTLSFromFile("certs/server1.pem", "certs/server1.key")
		if err != nil {
			log.Fatalf("failed to create credentials: %v", err)
		}

		opts = append(opts, grpc.Creds(creds))
	}

	s := grpc.NewServer(opts...)
	pb.RegisterTodoServiceServer(s, &testServer{})
	go s.Serve(info.Listener)
	return func() {
		s.Stop()
	}
}
