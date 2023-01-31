namespace Goldilocks;

public class Todo
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public DateOnly? DueBy { get; set; }

    public bool IsComplete { get; set; }
}

static class Todos
{
    internal readonly static Todo[] AllTodos = new Todo[]
        {
            new Todo() { Id = 0, Title = "Wash the dishes.", DueBy = DateOnly.FromDateTime(DateTime.Now), IsComplete = true },
            new Todo() { Id = 1, Title = "Dry the dishes.", DueBy = DateOnly.FromDateTime(DateTime.Now), IsComplete = true },
            new Todo() { Id = 2, Title = "Turn the dishes over.", DueBy = DateOnly.FromDateTime(DateTime.Now), IsComplete = false },
            new Todo() { Id = 3, Title = "Walk the kangaroo.", DueBy = DateOnly.FromDateTime(DateTime.Now.AddDays(1)), IsComplete = false },
            new Todo() { Id = 4, Title = "Call Grandma.", DueBy = DateOnly.FromDateTime(DateTime.Now.AddDays(1)), IsComplete = false },
        };
}