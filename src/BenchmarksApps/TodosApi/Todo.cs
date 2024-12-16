using System.ComponentModel.DataAnnotations;
using Nanorm;

namespace TodosApi;

[DataRecordMapper]
internal sealed partial class Todo : IValidatable
{
    public int Id { get; set; }

    public string Title { get; set; } = default!;

    public DateOnly? DueBy { get; set; }

    public bool IsComplete { get; set; }

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
