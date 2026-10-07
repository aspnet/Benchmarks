package main

import (
	"context"
	"errors"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"syscall"
	"time"
)

const shutdownTimeout = 8 * time.Second

func main() {
	os.Exit(run())
}

func run() int {
	logger := log.New(os.Stderr, "", log.LstdFlags)
	port, err := configuredPort()
	if err != nil {
		logger.Printf("Invalid configuration: %v", err)
		return 1
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	server := &http.Server{
		Handler:           newHandler(logger),
		ReadHeaderTimeout: 5 * time.Second,
		IdleTimeout:       60 * time.Second,
		ErrorLog:          logger,
	}
	listener, err := net.Listen("tcp4", net.JoinHostPort("0.0.0.0", strconv.Itoa(port)))
	if err != nil {
		logger.Printf("Could not listen on port %d: %v", port, err)
		return 1
	}
	logger.Printf("Listening on %s", listener.Addr())
	serveResult := make(chan error, 1)
	go func() {
		serveResult <- server.Serve(listener)
	}()

	if _, err := fmt.Fprintln(os.Stdout, "Application started."); err != nil {
		logger.Printf("Could not announce readiness: %v", err)
		if err := server.Close(); err != nil {
			logger.Printf("Could not close server: %v", err)
		}
		return 1
	}

	select {
	case err := <-serveResult:
		logger.Printf("HTTP server stopped unexpectedly: %v", err)
		return 1
	case <-ctx.Done():
		stop()
	}

	logger.Print("Shutdown requested.")
	shutdownCtx, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
	defer cancel()
	if err := server.Shutdown(shutdownCtx); err != nil {
		logger.Printf("Graceful shutdown failed; forcing connections closed: %v", err)
		if err := server.Close(); err != nil {
			logger.Printf("Could not close server: %v", err)
		}
		return 1
	}
	if err := <-serveResult; err != nil && !errors.Is(err, http.ErrServerClosed) {
		logger.Printf("HTTP server failed during shutdown: %v", err)
		return 1
	}
	logger.Print("Application stopped.")
	return 0
}

func configuredPort() (int, error) {
	value, present := os.LookupEnv("PORT")
	if !present {
		return 8080, nil
	}
	return parsePort(value)
}

func parsePort(value string) (int, error) {
	if value == "" {
		return 0, fmt.Errorf("PORT must contain decimal digits in the range 1 through 65535; got %q", value)
	}
	for _, digit := range value {
		if digit < '0' || digit > '9' {
			return 0, fmt.Errorf("PORT must contain decimal digits in the range 1 through 65535; got %q", value)
		}
	}
	port, err := strconv.Atoi(value)
	if err != nil || port < 1 || port > 65535 {
		return 0, fmt.Errorf("PORT must be in the range 1 through 65535; got %q", value)
	}
	return port, nil
}
