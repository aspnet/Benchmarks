package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"mime"
	"net/http"
	"net/http/httptest"
	"os"
	"strconv"
	"strings"
	"testing"
	"time"
)

func golden(t *testing.T) ([]byte, []json.RawMessage) {
	t.Helper()
	body, err := os.ReadFile("testdata/minimal-todo-v1.expected.json")
	if err != nil {
		t.Fatal(err)
	}
	var compact bytes.Buffer
	if err := json.Compact(&compact, body); err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(body, compact.Bytes()) {
		t.Fatal("golden fixture must be compact JSON without a trailing newline")
	}
	var items []json.RawMessage
	if err := json.Unmarshal(body, &items); err != nil {
		t.Fatal(err)
	}
	if len(items) != 5 {
		t.Fatalf("fixture has %d records, want 5", len(items))
	}
	return body, items
}

func testServer(t *testing.T) (*httptest.Server, *http.Client) {
	t.Helper()
	server := httptest.NewServer(newHandler(log.New(io.Discard, "", 0)))
	t.Cleanup(server.Close)
	transport := &http.Transport{DisableCompression: true}
	t.Cleanup(transport.CloseIdleConnections)
	client := &http.Client{
		Transport: transport,
		Timeout:   3 * time.Second,
		CheckRedirect: func(r *http.Request, via []*http.Request) error {
			return http.ErrUseLastResponse
		},
	}
	return server, client
}

func request(t *testing.T, client *http.Client, method, url string, headers map[string]string) (*http.Response, []byte) {
	t.Helper()
	req, err := http.NewRequest(method, url, nil)
	if err != nil {
		t.Fatal(err)
	}
	for name, value := range headers {
		req.Header.Set(name, value)
	}
	res, err := client.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	body, err := io.ReadAll(res.Body)
	closeErr := res.Body.Close()
	if err != nil {
		t.Fatal(err)
	}
	if closeErr != nil {
		t.Fatal(closeErr)
	}
	return res, body
}

func checkResponse(t *testing.T, res *http.Response, body []byte, status int, want []byte, mediaType string) {
	t.Helper()
	if res.StatusCode != status {
		t.Errorf("status = %d, want %d", res.StatusCode, status)
	}
	if !bytes.Equal(body, want) {
		t.Errorf("body = %q, want %q", body, want)
	}
	if mediaType != "" {
		got, params, err := mime.ParseMediaType(res.Header.Get("Content-Type"))
		if err != nil || got != mediaType {
			t.Errorf("Content-Type = %q, want %s: %v", res.Header.Get("Content-Type"), mediaType, err)
		}
		for key, value := range params {
			if key != "charset" || !strings.EqualFold(value, "utf-8") {
				t.Errorf("unexpected content type parameter %s=%s", key, value)
			}
		}
	}
	if res.Header.Get("Content-Encoding") != "" {
		t.Error("compression must remain disabled")
	}
	if res.Header.Get("Location") != "" {
		t.Error("response must not redirect")
	}
}

func TestHTTPContract(t *testing.T) {
	list, items := golden(t)
	server, client := testServer(t)
	t.Run("list", func(t *testing.T) {
		res, body := request(t, client, http.MethodGet, server.URL+"/todos", nil)
		checkResponse(t, res, body, http.StatusOK, list, "application/json")
	})
	for i, item := range items {
		t.Run(fmt.Sprintf("item-%d", i+1), func(t *testing.T) {
			res, body := request(t, client, http.MethodGet, server.URL+"/todos/"+strconv.Itoa(i+1), nil)
			checkResponse(t, res, body, http.StatusOK, item, "application/json")
		})
	}
	for _, path := range []string{
		"/todos/abc", "/todos/0", "/todos/01", "/todos/-1", "/todos/+1",
		"/todos/6", "/todos/999999999999999999999999999999", "/todos/1.0",
		"/todos/1e0", "/todos/%201", "/todos/1%20", "/todos/%EF%BC%91",
		"/", "/unknown", "/todos/1/extra", "/other/todos",
		"/unknown//path", "/unknown/../missing", "/todos//1",
	} {
		t.Run("missing-"+path, func(t *testing.T) {
			res, body := request(t, client, http.MethodGet, server.URL+path, nil)
			checkResponse(t, res, body, http.StatusNotFound, nil, "")
		})
	}
	for _, method := range []string{http.MethodPost, http.MethodPut, http.MethodPatch, http.MethodDelete} {
		paths := []string{"/todos"}
		for i := 1; i <= 5; i++ {
			paths = append(paths, "/todos/"+strconv.Itoa(i))
		}
		for _, path := range paths {
			t.Run(method+"-"+path, func(t *testing.T) {
				res, body := request(t, client, method, server.URL+path, nil)
				checkResponse(t, res, body, http.StatusMethodNotAllowed, nil, "")
				if res.Header.Get("Allow") != http.MethodGet {
					t.Errorf("Allow = %q, want GET", res.Header.Get("Allow"))
				}
			})
		}
	}
	t.Run("ready", func(t *testing.T) {
		res, body := request(t, client, http.MethodGet, server.URL+"/healthz", nil)
		checkResponse(t, res, body, http.StatusOK, []byte("ready"), "text/plain")
	})
	for i, path := range []string{"/todos", "/todos/1", "/healthz"} {
		t.Run("no-compression-or-negotiation-"+path, func(t *testing.T) {
			res, body := request(t, client, http.MethodGet, server.URL+path, map[string]string{
				"Accept-Encoding":   "gzip, deflate, br",
				"Accept":            "text/html",
				"If-None-Match":     "*",
				"If-Modified-Since": "Wed, 01 Jan 2031 00:00:00 GMT",
			})
			want := [][]byte{list, items[0], []byte("ready")}[i]
			mediaType := "application/json"
			if i == 2 {
				mediaType = "text/plain"
			}
			checkResponse(t, res, body, http.StatusOK, want, mediaType)
			for _, header := range []string{"ETag", "Last-Modified", "Cache-Control", "Expires", "Vary"} {
				if res.Header.Get(header) != "" {
					t.Errorf("unexpected caching/negotiation header %s", header)
				}
			}
		})
	}
	t.Run("unchanged-after-mutations", func(t *testing.T) {
		res, body := request(t, client, http.MethodGet, server.URL+"/todos", nil)
		checkResponse(t, res, body, http.StatusOK, list, "application/json")
	})
}

func TestConcurrentReads(t *testing.T) {
	list, items := golden(t)
	server, client := testServer(t)
	t.Run("workers", func(t *testing.T) {
		for i := 0; i < 16; i++ {
			t.Run(strconv.Itoa(i), func(t *testing.T) {
				t.Parallel()
				for j := 0; j < 20; j++ {
					res, body := request(t, client, http.MethodGet, server.URL+"/todos", nil)
					checkResponse(t, res, body, http.StatusOK, list, "application/json")
					id := (j % len(items)) + 1
					res, body = request(t, client, http.MethodGet, server.URL+"/todos/"+strconv.Itoa(id), nil)
					checkResponse(t, res, body, http.StatusOK, items[id-1], "application/json")
				}
			})
		}
	})
	res, body := request(t, client, http.MethodGet, server.URL+"/todos", nil)
	checkResponse(t, res, body, http.StatusOK, list, "application/json")
}

type failingWriter struct {
	header http.Header
	status int
}

func (w *failingWriter) Header() http.Header    { return w.header }
func (w *failingWriter) WriteHeader(status int) { w.status = status }
func (w *failingWriter) Write(body []byte) (int, error) {
	return 0, fmt.Errorf("test connection failure")
}

func TestUnexpectedErrorsAreLogged(t *testing.T) {
	var logs bytes.Buffer
	logger := log.New(&logs, "", 0)
	handler := newHandler(logger)
	for _, path := range []string{"/todos", "/healthz"} {
		w := &failingWriter{header: make(http.Header)}
		handler.ServeHTTP(w, httptest.NewRequest(http.MethodGet, path, nil))
		if !strings.Contains(logs.String(), "test connection failure") {
			t.Fatal("write failure was not logged")
		}
		logs.Reset()
	}
	w := httptest.NewRecorder()
	writeJSON(w, make(chan int), logger)
	if w.Code != http.StatusInternalServerError || w.Body.Len() != 0 {
		t.Fatalf("serialization failure returned %d %q", w.Code, w.Body.String())
	}
	if !strings.Contains(logs.String(), "Could not serialize") {
		t.Fatal("serialization failure was not logged")
	}
	logs.Reset()
	handler.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/todos", nil))
	if logs.Len() != 0 {
		t.Fatalf("routine request unexpectedly logged: %s", logs.String())
	}
}
