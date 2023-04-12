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

        group.AddValidationFilter();

        // BUG: Having to call ToListAsync() on query results until JSON support for unspeakable types (https://github.com/dotnet/aspnetcore/issues/47548) is resolved

        group.MapGet("/", (NpgsqlDataSource db, CancellationToken ct) =>
            db.QueryAsync<Todo>("SELECT * FROM Todos", ct).ToListAsync(ct))
            .WithName("GetAllTodos");

        group.MapGet("/complete", (NpgsqlDataSource db, CancellationToken ct) =>
            db.QueryAsync<Todo>("SELECT * FROM Todos WHERE IsComplete = true", ct).ToListAsync(ct))
            .WithName("GetCompleteTodos");

        group.MapGet("/incomplete", (NpgsqlDataSource db, CancellationToken ct) =>
            db.QueryAsync<Todo>("SELECT * FROM Todos WHERE IsComplete = false", ct).ToListAsync(ct))
            .WithName("GetIncompleteTodos");

        group.MapGet("/{id:int}", async Task<Results<Ok<Todo>, NotFound>> (int id, NpgsqlDataSource db, CancellationToken ct) =>
            await db.QuerySingleAsync<Todo>(
                "SELECT * FROM Todos WHERE Id = $1", ct, id.AsTypedDbParameter())
                is Todo todo
                    ? TypedResults.Ok(todo)
                    : TypedResults.NotFound())
            .WithName("GetTodoById");

        group.MapGet("/find", async Task<Results<Ok<Todo>, NotFound>> (string title, bool? isComplete, NpgsqlDataSource db, CancellationToken ct) =>
            await db.QuerySingleAsync<Todo>(
                "SELECT * FROM Todos WHERE LOWER(Title) = LOWER($1) AND ($2 is NULL OR IsComplete = $2)",
                ct,
                title.AsTypedDbParameter(),
                isComplete.AsTypedDbParameter())
                is Todo todo
                    ? TypedResults.Ok(todo)
                    : TypedResults.NotFound())
            .WithName("FindTodo");

        group.MapPost("/", async Task<Created<Todo>> (Todo todo, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var createdTodo = await db.QuerySingleAsync<Todo>(
                "INSERT INTO Todos(Title, IsComplete) Values($1, $2) RETURNING *",
                ct,
                todo.Title.AsTypedDbParameter(),
                todo.IsComplete.AsTypedDbParameter());

            return TypedResults.Created($"/todos/{createdTodo?.Id}", createdTodo);
        })
        .WithName("CreateTodo");

        group.MapPut("/{id}", async Task<Results<NoContent, NotFound>> (int id, Todo inputTodo, NpgsqlDataSource db, CancellationToken ct) =>
        {
            inputTodo.Id = id;
            
            return await db.ExecuteAsync(
                "UPDATE Todos SET Title = $1, IsComplete = $2 WHERE Id = $3",
                ct,
                inputTodo.Title.AsTypedDbParameter(),
                inputTodo.IsComplete.AsTypedDbParameter(),
                id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        })
        .WithName("UpdateTodo");

        group.MapPut("/{id}/mark-complete", async Task<Results<NoContent, NotFound>> (int id, NpgsqlDataSource db, CancellationToken ct) =>
            await db.ExecuteAsync(
                "UPDATE Todos SET IsComplete = true WHERE Id = $1", ct, id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("MarkComplete");

        group.MapPut("/{id}/mark-incomplete", async Task<Results<NoContent, NotFound>> (int id, NpgsqlDataSource db, CancellationToken ct) =>
            await db.ExecuteAsync(
                "UPDATE Todos SET IsComplete = false WHERE Id = $1", ct, id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("MarkIncomplete");

        group.MapDelete("/{id}", async Task<Results<NoContent, NotFound>> (int id, NpgsqlDataSource db, CancellationToken ct) =>
            await db.ExecuteAsync(
                "DELETE FROM Todos WHERE Id = $1", ct, id.AsTypedDbParameter()) == 1
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
        .WithName("DeleteTodo");

        group.MapDelete("/delete-all", async (NpgsqlDataSource db, CancellationToken ct) =>
            TypedResults.Ok(await db.ExecuteAsync("DELETE FROM Todos", ct)))
            .WithName("DeleteAll")
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("admin"));

        return group;
    }
}

[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(List<Todo>))]
[JsonSerializable(typeof(IAsyncEnumerable<Todo>))]
internal partial class TodoApiJsonSerializerContext : JsonSerializerContext
{

}
