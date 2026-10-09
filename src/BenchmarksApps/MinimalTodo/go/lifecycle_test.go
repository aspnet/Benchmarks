package main

import (
	"bufio"
	"bytes"
	"context"
	"errors"
	"io"
	"net"
	"net/http"
	"net/http/httptrace"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"syscall"
	"testing"
	"time"
)

func TestPortConfiguration(t *testing.T) {
	for _, value := range []string{"", "0", "-1", "+80", " 80", "80 ", "1.5", "1e3", "abc", "65536", "999999999999999999999", "８０"} {
		t.Run("invalid-"+value, func(t *testing.T) {
			if _, err := parsePort(value); err == nil {
				t.Errorf("accepted invalid PORT=%q", value)
			}
		})
	}
	for value, want := range map[string]int{"1": 1, "8080": 8080, "65535": 65535, "00080": 80} {
		t.Run("valid-"+value, func(t *testing.T) {
			if got, err := parsePort(value); err != nil || got != want {
				t.Errorf("PORT=%q: got %d, %v; want %d", value, got, err, want)
			}
		})
	}
	t.Run("unset-default", func(t *testing.T) {
		t.Setenv("PORT", "not-used")
		if err := os.Unsetenv("PORT"); err != nil {
			t.Fatal(err)
		}
		if got, err := configuredPort(); err != nil || got != 8080 {
			t.Fatalf("unset PORT: got %d, %v; want 8080", got, err)
		}
	})
}

func TestServiceProcess(t *testing.T) {
	if os.Getenv("TODO_TEST_PROCESS") == "1" {
		os.Exit(run())
	}
}

func serviceCommand(t *testing.T, port string) *exec.Cmd {
	t.Helper()
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	args := []string{"-test.run=^TestServiceProcess$"}
	if binary := os.Getenv("TODO_TEST_BINARY"); binary != "" {
		executable, err = filepath.Abs(binary)
		if err != nil {
			t.Fatal(err)
		}
		args = nil
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	cmd := exec.CommandContext(ctx, executable, args...)
	for _, entry := range os.Environ() {
		if !strings.HasPrefix(entry, "PORT=") && !strings.HasPrefix(entry, "TODO_TEST_PROCESS=") {
			cmd.Env = append(cmd.Env, entry)
		}
	}
	cmd.Env = append(cmd.Env, "TODO_TEST_PROCESS=1", "PORT="+port)
	return cmd
}

func assertStartupFailure(t *testing.T, port, diagnostic string) {
	t.Helper()
	cmd := serviceCommand(t, port)
	output, err := cmd.CombinedOutput()
	var exitErr *exec.ExitError
	if !errors.As(err, &exitErr) || exitErr.ExitCode() != 1 {
		t.Fatalf("wanted exit 1, got %v; output: %s", err, output)
	}
	if strings.Contains(string(output), "Application started.") {
		t.Fatalf("announced readiness after failed startup: %s", output)
	}
	if !strings.Contains(string(output), diagnostic) {
		t.Fatalf("missing useful %q diagnostic: %s", diagnostic, output)
	}
}

func TestInvalidPortStartup(t *testing.T) {
	for _, port := range []string{"", "0", "65536", "-1", "+8080", " 8080", "abc", "9999999999999999999999999"} {
		t.Run(port, func(t *testing.T) {
			assertStartupFailure(t, port, "Invalid configuration:")
		})
	}
}

func TestOccupiedPortStartup(t *testing.T) {
	listener, err := net.Listen("tcp4", "0.0.0.0:0")
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	port := listener.Addr().(*net.TCPAddr).Port
	assertStartupFailure(t, strconv.Itoa(port), "Could not listen on port")
}

func TestProcessLifecycle(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("POSIX signal lifecycle requires Linux or another Unix platform")
	}
	for _, signal := range []os.Signal{syscall.SIGTERM, os.Interrupt} {
		t.Run(signal.String(), func(t *testing.T) {
			list, _ := golden(t)
			listener, err := net.Listen("tcp4", "127.0.0.1:0")
			if err != nil {
				t.Fatal(err)
			}
			port := listener.Addr().(*net.TCPAddr).Port
			if err := listener.Close(); err != nil {
				t.Fatal(err)
			}
			cmd := serviceCommand(t, strconv.Itoa(port))
			var stderr bytes.Buffer
			cmd.Stderr = &stderr
			stdout, err := cmd.StdoutPipe()
			if err != nil {
				t.Fatal(err)
			}
			if err := cmd.Start(); err != nil {
				t.Fatal(err)
			}
			waited := false
			defer func() {
				if !waited {
					_ = cmd.Process.Kill()
					_ = cmd.Wait()
				}
			}()
			scanner := bufio.NewScanner(stdout)
			if !scanner.Scan() || scanner.Text() != "Application started." {
				t.Fatalf("missing exact readiness line; scanner error: %v", scanner.Err())
			}
			transport := &http.Transport{DisableCompression: true}
			defer transport.CloseIdleConnections()
			client := &http.Client{
				Transport: transport,
				Timeout:   3 * time.Second,
				CheckRedirect: func(r *http.Request, via []*http.Request) error {
					return http.ErrUseLastResponse
				},
			}
			base := "http://127.0.0.1:" + strconv.Itoa(port)
			res, body := request(t, client, http.MethodGet, base+"/healthz", nil)
			checkResponse(t, res, body, http.StatusOK, []byte("ready"), "text/plain")
			for i := 0; i < 3; i++ {
				reused := false
				trace := &httptrace.ClientTrace{
					GotConn: func(info httptrace.GotConnInfo) { reused = info.Reused },
				}
				req, err := http.NewRequestWithContext(httptrace.WithClientTrace(context.Background(), trace), http.MethodGet, base+"/todos", nil)
				if err != nil {
					t.Fatal(err)
				}
				res, err := client.Do(req)
				if err != nil {
					t.Fatal(err)
				}
				body, err := io.ReadAll(res.Body)
				closeErr := res.Body.Close()
				if err != nil || closeErr != nil {
					t.Fatalf("reading body: %v; closing: %v", err, closeErr)
				}
				checkResponse(t, res, body, http.StatusOK, list, "application/json")
				if res.ProtoMajor != 1 || res.ProtoMinor != 1 || res.Close || !reused {
					t.Errorf("expected persistent HTTP/1.1: protocol=%s close=%v reused=%v", res.Proto, res.Close, reused)
				}
			}
			start := time.Now()
			if err := cmd.Process.Signal(signal); err != nil {
				t.Fatal(err)
			}
			for scanner.Scan() {
				t.Errorf("unexpected extra stdout: %q", scanner.Text())
			}
			if err := scanner.Err(); err != nil {
				t.Error(err)
			}
			err = cmd.Wait()
			waited = true
			if err != nil {
				t.Fatalf("graceful termination failed or was forced: %v; stderr: %s", err, stderr.String())
			}
			if elapsed := time.Since(start); elapsed >= 10*time.Second {
				t.Errorf("shutdown exceeded 10 seconds: %s", elapsed)
			}
			if !strings.Contains(stderr.String(), "Shutdown requested.") || !strings.Contains(stderr.String(), "Application stopped.") {
				t.Errorf("missing lifecycle logs: %s", stderr.String())
			}
			if strings.Contains(stderr.String(), "forcing") {
				t.Errorf("shutdown was forced: %s", stderr.String())
			}
			conn, err := net.DialTimeout("tcp4", "127.0.0.1:"+strconv.Itoa(port), time.Second)
			if err == nil {
				conn.Close()
				t.Error("listener still accepts connections after exit")
			}
		})
	}
}
