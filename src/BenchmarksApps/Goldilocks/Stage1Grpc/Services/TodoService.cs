using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Goldilocks.Services;

public class TodoServiceImpl : TodoService.TodoServiceBase
{
    internal readonly static Todo[] AllTodos = new Todo[]
    {
        new Todo() { Id = 0, Title = "Wash the dishes.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow), IsComplete = true },
        new Todo() { Id = 1, Title = "Dry the dishes.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow), IsComplete = true },
        new Todo() { Id = 2, Title = "Turn the dishes over.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow), IsComplete = false },
        new Todo() { Id = 3, Title = "Walk the kangaroo.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1)), IsComplete = false },
        new Todo() { Id = 4, Title = "Call Grandma.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1)), IsComplete = false },
    };

    public override Task<GetAllTodosResponse> GetAllTodos(GetAllTodosRequest request, ServerCallContext context)
    {
        var response = new GetAllTodosResponse();
        response.AllTodos.AddRange(AllTodos);

        return Task.FromResult(response);
    }

    public override Task<GetTodoResponse> GetTodo(GetTodoRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetTodoResponse
        {
            Todo = AllTodos.FirstOrDefault(a => a.Id == request.Id)
        });
    }
}