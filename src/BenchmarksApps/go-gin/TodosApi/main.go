package main

import (
	"database/sql"
	"log"
	"net/http"
	"os"

	_ "github.com/lib/pq"

	"github.com/gin-gonic/gin"
)

func main() {
	connStr := os.Getenv("DATABASE_CONNECTION")
	listenAddr := os.Getenv("LISTEN_ADDRESS")

	if connStr == "" {
		log.Fatalln("DATABASE_CONNECTION environment variable not set")
	}

	if listenAddr == "" {
		listenAddr = ":8080"
	}

	db, err := sql.Open("postgres", connStr)
	if err != nil {
		log.Fatal(err)
	}

	todoapi := &todoapi{db}

	router := gin.Default()

	router.GET("/favicon.ico", func(c *gin.Context) {
		c.String(http.StatusNotFound, "")
	})

	todoapi.MapTodoApi(router)

	router.Run(listenAddr)
}
