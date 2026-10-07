package main

import (
	"encoding/json"
	"log"
	"net/http"
	"path"
	"strconv"
)

type todo struct {
	ID         int     `json:"id"`
	Title      string  `json:"title"`
	DueBy      *string `json:"dueBy"`
	IsComplete bool    `json:"isComplete"`
}

func newHandler(logger *log.Logger) http.Handler {
	first, second, third := "2026-01-01", "2026-01-02", "2026-01-03"
	// Go has no const collections; this private array is captured read-only.
	todos := [...]todo{
		{ID: 1, Title: "Walk the dog"},
		{ID: 2, Title: "Do the dishes", DueBy: &first},
		{ID: 3, Title: "Do the laundry", DueBy: &second},
		{ID: 4, Title: "Clean the bathroom"},
		{ID: 5, Title: "Clean the car", DueBy: &third},
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/todos", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			methodNotAllowed(w)
			return
		}
		writeJSON(w, todos, logger)
	})
	mux.HandleFunc("/todos/{id}", func(w http.ResponseWriter, r *http.Request) {
		rawID := r.PathValue("id")
		id, err := strconv.Atoi(rawID)
		if err != nil || id < 1 || id > len(todos) || rawID != strconv.Itoa(id) {
			w.WriteHeader(http.StatusNotFound)
			return
		}
		if r.Method != http.MethodGet {
			methodNotAllowed(w)
			return
		}
		writeJSON(w, todos[id-1], logger)
	})
	mux.HandleFunc("/healthz", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			methodNotAllowed(w)
			return
		}
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		if _, err := w.Write([]byte("ready")); err != nil {
			logger.Printf("Could not write readiness response: %v", err)
		}
	})
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		// Reject noncanonical paths before ServeMux can redirect them.
		if r.URL.Path != path.Clean(r.URL.Path) {
			w.WriteHeader(http.StatusNotFound)
			return
		}
		mux.ServeHTTP(w, r)
	})
}

func methodNotAllowed(w http.ResponseWriter) {
	w.Header().Set("Allow", http.MethodGet)
	w.WriteHeader(http.StatusMethodNotAllowed)
}

func writeJSON(w http.ResponseWriter, value any, logger *log.Logger) {
	body, err := json.Marshal(value)
	if err != nil {
		logger.Printf("Could not serialize Todo response: %v", err)
		w.WriteHeader(http.StatusInternalServerError)
		return
	}
	w.Header().Set("Content-Type", "application/json")
	if _, err := w.Write(body); err != nil {
		logger.Printf("Could not write Todo response: %v", err)
	}
}
