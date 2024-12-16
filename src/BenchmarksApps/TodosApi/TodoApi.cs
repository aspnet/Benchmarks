using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nanorm;
using Npgsql;
using TodosApi;

namespace Microsoft.AspNetCore.Routing;

internal static class TodoApi
{
    public static RouteGroupBuilder MapTodoApi(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/todos");

        group.AddValidationFilter();

        group.MapGet("/", ([FromServices] NpgsqlDataSource db, CancellationToken ct) => db.QueryAsync<Todo>("SELECT * FROM Todos", ct))
            .WithName("GetAllTodos");

        group.MapGet("/complete", ([FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            db.QueryAsync<Todo>("SELECT * FROM Todos WHERE IsComplete = true", ct))
            .WithName("GetCompleteTodos");

        group.MapGet("/incomplete", ([FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            db.QueryAsync<Todo>("SELECT * FROM Todos WHERE IsComplete = false", ct))
            .WithName("GetIncompleteTodos");

        group.MapGet("/{id:int}", async Task<Results<Ok<Todo>, NotFound>> (int id, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            await db.QuerySingleAsync<Todo>($"SELECT * FROM Todos WHERE Id = {id}", ct)
                is Todo todo
                    ? TypedResults.Ok(todo)
                    : TypedResults.NotFound())
            .WithName("GetTodoById");

        group.MapGet("/find", async Task<Results<Ok<Todo>, NotFound>> (string title, bool? isComplete, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            await db.QuerySingleAsync<Todo>($"""
                SELECT * FROM Todos
                WHERE LOWER(Title) = LOWER({title})
                  AND ({isComplete} is NULL OR IsComplete = {isComplete})
                """, ct)
                is Todo todo
                    ? TypedResults.Ok(todo)
                    : TypedResults.NotFound())
            .WithName("FindTodo");

        group.MapPost("/", async Task<Created<Todo>> (Todo todo, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
        {
            var createdTodo = await db.QuerySingleAsync<Todo>(
                $"INSERT INTO Todos(Title, IsComplete) Values({todo.Title}, {todo.IsComplete}) RETURNING *", ct);

            return TypedResults.Created($"/todos/{createdTodo?.Id}", createdTodo);
        })
        .WithName("CreateTodo");

        group.MapPut("/{id}", async Task<Results<NoContent, NotFound>> (int id, Todo inputTodo, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
        {
            inputTodo.Id = id;

            return await db.ExecuteAsync($"""
                UPDATE Todos
                SET Title = {inputTodo.Title}, IsComplete = {inputTodo.IsComplete}
                WHERE Id = {id}
                """, ct) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        })
        .WithName("UpdateTodo");

        group.MapPut("/{id}/mark-complete", async Task<Results<NoContent, NotFound>> (int id, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            await db.ExecuteAsync($"UPDATE Todos SET IsComplete = true WHERE Id = {id}", ct) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("MarkComplete");

        group.MapPut("/{id}/mark-incomplete", async Task<Results<NoContent, NotFound>> (int id, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            await db.ExecuteAsync($"UPDATE Todos SET IsComplete = false WHERE Id = {id}", ct) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("MarkIncomplete");

        group.MapDelete("/{id}", async Task<Results<NoContent, NotFound>> (int id, [FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            await db.ExecuteAsync($"DELETE FROM Todos WHERE Id = {id}", ct) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("DeleteTodo");

        group.MapDelete("/delete-all", async ([FromServices] NpgsqlDataSource db, CancellationToken ct) =>
            TypedResults.Ok(await db.ExecuteAsync("DELETE FROM Todos", ct)))
            .WithName("DeleteAll")
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("admin"));

        return group;
    }
}

[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(IAsyncEnumerable<Todo>))]
internal partial class TodoApiJsonSerializerContext : JsonSerializerContext
{

}
