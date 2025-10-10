package main

import (
	"database/sql"
	"fmt"
	"net/http"
	"strconv"

	"github.com/gin-gonic/gin"
)

type HttpValidationProblemDetails struct {
	Type     *string             `json:"type,omitempty"`
	Title    *string             `json:"title,omitempty"`
	Status   *int32              `json:"status,omitempty"`
	Detail   *string             `json:"detail,omitempty"`
	Instance *string             `json:"instance,omitempty"`
	Errors   map[string][]string `json:"errors,omitempty"`
}

type Todo struct {
	ID         int32   `json:"id"`
	Title      *string `json:"title,omitempty"`
	DueBy      *string `json:"dueBy,omitempty"`
	IsComplete bool    `json:"isComplete"`
}

type todoapi struct {
	db *sql.DB
}

func (t *todoapi) Query(query string, args ...any) ([]Todo, error) {
	rows, err := t.db.Query(query, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var todos []Todo
	for rows.Next() {
		var todo Todo
		if err := rows.Scan(&todo.ID, &todo.Title, &todo.DueBy, &todo.IsComplete); err != nil {
			return nil, err
		}
		todos = append(todos, todo)
	}
	return todos, nil
}

func (t *todoapi) QueryRow(query string, args ...any) (*Todo, error) {
	row := t.db.QueryRow(query, args...)

	var todo Todo
	if err := row.Scan(&todo.ID, &todo.Title, &todo.DueBy, &todo.IsComplete); err != nil {
		return nil, err
	}
	return &todo, nil
}

func (t *todoapi) Exec(query string, args ...any) (int64, error) {
	result, err := t.db.Exec(query, args)
	if err != nil {
		return 0, err
	}

	return result.RowsAffected()
}

func (t *todoapi) MapTodoApi(router gin.IRouter) {
	group := router.Group("/api/todos")

	group.GET("/", func(c *gin.Context) {

		todos, err := t.Query("SELECT * FROM Todos")
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		c.JSON(http.StatusOK, todos)
	})

	group.GET("/complete", func(c *gin.Context) {

		todos, err := t.Query("SELECT * FROM Todos WHERE IsComplete = true")
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		c.JSON(http.StatusOK, todos)
	})

	group.GET("/incomplete", func(c *gin.Context) {

		todos, err := t.Query("SELECT * FROM Todos WHERE IsComplete = false")
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		c.JSON(http.StatusOK, todos)
	})

	group.GET("/:id", func(c *gin.Context) {

		id := c.Param("id")
		todo, err := t.QueryRow("SELECT * FROM Todos WHERE Id = ?", id)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		if todo != nil {
			c.JSON(http.StatusOK, todo)
		} else {
			c.Status(http.StatusNotFound)
		}
	})

	group.GET("/find", func(c *gin.Context) {

		title := c.Query("title")
		isComplete := c.Query("isComplete")
		todo, err := t.QueryRow(`
		SELECT * FROM Todos
		WHERE LOWER(Title) = LOWER(?)
			AND (? IS NULL OR IsComplete = ?)
		`, title, isComplete, isComplete)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		if todo != nil {
			c.JSON(http.StatusOK, todo)
		} else {
			c.Status(http.StatusNotFound)
		}
	})

	group.POST("/", func(c *gin.Context) {

		var todo Todo
		if err := c.ShouldBindJSON(&todo); err != nil {
			c.AbortWithError(http.StatusBadRequest, err)
			return
		}
		createdTodo, err := t.QueryRow(`
				INSERT INTO Todos(Title, IsComplete)
				VALUES (%s, %s)
				RETURNING *
			`, todo.Title, todo.IsComplete)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		location := fmt.Sprintf("/todos/%d", createdTodo.ID)
		c.JSON(http.StatusCreated, gin.H{"location": location, "data": createdTodo})
	})

	group.PUT("/:id", func(c *gin.Context) {

		var inputTodo Todo
		if err := c.ShouldBindJSON(&inputTodo); err != nil {
			c.AbortWithError(http.StatusBadRequest, err)
			return
		}

		id, err := strconv.ParseInt(c.Param("id"), 10, 32)
		if err != nil {
			c.AbortWithError(http.StatusBadRequest, err)
			return
		}

		inputTodo.ID = int32(id)
		result, err := t.Exec(`
		UPDATE Todos
		SET Title = ?, IsComplete = ?
		WHERE Id = ?
		`, inputTodo.Title, inputTodo.IsComplete, id)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		if result == 1 {
			c.Status(http.StatusNoContent)
		} else {
			c.Status(http.StatusNotFound)
		}
	})

	group.PUT("/:id/mark-complete", func(c *gin.Context) {

		id := c.Param("id")
		result, err := t.Exec("UPDATE Todos SET IsComplete = true WHERE Id = ?", id)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		if result == 1 {
			c.Status(http.StatusNoContent)
		} else {
			c.Status(http.StatusNotFound)
		}
	})

	group.PUT("/:id/mark-incomplete", func(c *gin.Context) {

		id := c.Param("id")
		result, err := t.Exec("UPDATE Todos SET IsComplete = false WHERE Id = ?", id)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		if result == 1 {
			c.Status(http.StatusNoContent)
		} else {
			c.Status(http.StatusNotFound)
		}
	})

	group.DELETE("/:id", func(c *gin.Context) {
		id := c.Param("id")
		result, err := t.Exec("DELETE FROM Todos WHERE Id = ?", id)
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		if result == 1 {
			c.Status(http.StatusNoContent)
		} else {
			c.Status(http.StatusNotFound)
		}
	})

	// TODO: Add authentication
	// RequireAuthenticatdUser().RequireRole("admin")
	group.DELETE("/delete-all", func(c *gin.Context) {

		result, err := t.Exec("DELETE FROM Todos")
		if err != nil {
			c.AbortWithError(http.StatusInternalServerError, err)
			return
		}
		c.JSON(http.StatusOK, result)
	})
}
