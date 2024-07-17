using Basic;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace BasicGrpc.Services;

public class TodoServiceImpl : TodoService.TodoServiceBase
{
    private readonly static Todo[] AllTodos;
    private readonly static GetAllTodosResponse AllTodosResponse;

    static TodoServiceImpl()
    {
        AllTodos = new Todo[]
        {
            new Todo() { Id = 0, Title = "Wash the dishes.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow), IsComplete = true },
            new Todo() { Id = 1, Title = "Dry the dishes.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow), IsComplete = true },
            new Todo() { Id = 2, Title = "Turn the dishes over.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow), IsComplete = false },
            new Todo() { Id = 3, Title = "Walk the kangaroo.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1)), IsComplete = false },
            new Todo() { Id = 4, Title = "Call Grandma.", DueBy = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1)), IsComplete = false },
        };
        AllTodosResponse = new GetAllTodosResponse();
        AllTodosResponse.AllTodos.AddRange(AllTodos);
    }

    public override Task<GetAllTodosResponse> GetAllTodos(GetAllTodosRequest request, ServerCallContext context) =>
        Task.FromResult(AllTodosResponse);

    public override Task<GetTodoResponse> GetTodo(GetTodoRequest request, ServerCallContext context) =>
        Task.FromResult(new GetTodoResponse
        {
            Todo = AllTodos.FirstOrDefault(a => a.Id == request.Id)
        });
}