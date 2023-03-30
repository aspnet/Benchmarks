using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using TodosApi;

namespace Microsoft.AspNetCore.Routing;

internal static class TodoApi
{
    public static RouteGroupBuilder MapTodoApi(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/todos");

        group.MapGet("/", (NpgsqlDataSource db) => db.QueryAsync<Todo>("SELECT * FROM Todos"))
            .WithName("GetAllTodos");

        group.MapGet("/complete", (NpgsqlDataSource db) => db.QueryAsync<Todo>("SELECT * FROM Todos WHERE IsComplete = true"))
            .WithName("GetCompleteTodos");

        group.MapGet("/incomplete", (NpgsqlDataSource db) => db.QueryAsync<Todo>("SELECT * FROM Todos WHERE IsComplete = false"))
            .WithName("GetIncompleteTodos");

        group.MapGet("/{id:int}", async Task<Results<Ok<Todo>, NotFound>> (int id, NpgsqlDataSource db) =>
            await db.QuerySingleAsync<Todo>(
                "SELECT * FROM Todos WHERE Id = $1", id.AsTypedDbParameter())
                is Todo todo
                    ? TypedResults.Ok(todo)
                    : TypedResults.NotFound())
            .WithName("GetTodoById");

        group.MapGet("/find", async Task<Results<Ok<Todo>, NotFound>> (string title, bool? isComplete, NpgsqlDataSource db) =>
            await db.QuerySingleAsync<Todo>(
                "SELECT * FROM Todos WHERE LOWER(Title) = LOWER($1) AND ($2 is NULL OR IsComplete = $2)",
                title.AsTypedDbParameter(),
                isComplete.AsTypedDbParameter())
                is Todo todo
                    ? TypedResults.Ok(todo)
                    : TypedResults.NotFound())
            .WithName("FindTodo");

        group.MapPost("/", async Task<Results<Created<Todo>, ValidationProblem>> (Todo todo, NpgsqlDataSource db) =>
        {
            var createdTodo = await db.QuerySingleAsync<Todo>(
                "INSERT INTO Todos(Title, IsComplete) Values($1, $2) RETURNING *",
                todo.Title.AsTypedDbParameter(),
                todo.IsComplete.AsTypedDbParameter());

            return TypedResults.Created($"/todos/{createdTodo?.Id}", createdTodo);
        })
        .WithName("CreateTodo");

        group.MapPut("/{id}", async Task<Results<NoContent, NotFound, ValidationProblem>> (int id, Todo inputTodo, NpgsqlDataSource db) =>
        {
            inputTodo.Id = id;
            
            return await db.ExecuteAsync(
                "UPDATE Todos SET Title = $1, IsComplete = $2 WHERE Id = $3",
                inputTodo.Title.AsTypedDbParameter(),
                inputTodo.IsComplete.AsTypedDbParameter(),
                id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        })
        .WithName("UpdateTodo");

        group.MapPut("/{id}/mark-complete", async Task<Results<NoContent, NotFound>> (int id, NpgsqlDataSource db) =>
            await db.ExecuteAsync(
                "UPDATE Todos SET IsComplete = true WHERE Id = $1", id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("MarkComplete");

        group.MapPut("/{id}/mark-incomplete", async Task<Results<NoContent, NotFound>> (int id, NpgsqlDataSource db) =>
            await db.ExecuteAsync(
                "UPDATE Todos SET IsComplete = false WHERE Id = $1", id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("MarkIncomplete");

        group.MapDelete("/{id}", async Task<Results<NoContent, NotFound>> (int id, NpgsqlDataSource db) =>
            await db.ExecuteAsync(
                "DELETE FROM Todos WHERE Id = $1", id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("DeleteTodo");

        group.MapDelete("/delete-all", async (NpgsqlDataSource db) => TypedResults.Ok(await db.ExecuteAsync("DELETE FROM Todos")))
            .WithName("DeleteAll")
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("admin"));

        return group;
    }
}



[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(IAsyncEnumerable<Todo>))]
[JsonSerializable(typeof(IEnumerable<Todo>))]
internal partial class TodoApiJsonSerializerContext : JsonSerializerContext
{

}
