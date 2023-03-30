using Npgsql;

namespace TodosApi;

sealed class Todo : IDataReaderMapper<Todo>
{
    public int Id { get; set; }

    public string Title { get; set; } = default!;

    public DateOnly? DueBy { get; set; }

    public bool IsComplete { get; set; }

    public static Todo Map(NpgsqlDataReader dataReader)
    {
        return !dataReader.HasRows ? new() : new()
        {
            Id = dataReader.GetInt32(dataReader.GetOrdinal(nameof(Id))),
            Title = dataReader.GetString(dataReader.GetOrdinal(nameof(Title))),
            IsComplete = dataReader.GetBoolean(dataReader.GetOrdinal(nameof(IsComplete)))
        };
    }
}
