using System.ComponentModel.DataAnnotations;
using Nanorm.Npgsql;
using Npgsql;

namespace TodosApi;

internal sealed class Todo : IDataReaderMapper<Todo>, IValidatable
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(Title))
        {
            yield return new ValidationResult($"A value is required for {nameof(Title)}.", new[] { nameof(Title) });
            yield break;
        }
        if (DueBy.HasValue && DueBy.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            yield return new ValidationResult($"{nameof(DueBy)} cannot be in the past.", new[] { nameof(DueBy) });
        }
    }
}
