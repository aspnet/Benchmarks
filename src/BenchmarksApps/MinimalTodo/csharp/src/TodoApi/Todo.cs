using System.Text.Json.Serialization;

namespace TodoApi;

public sealed record Todo(
    [property: JsonPropertyOrder(0)] int Id,
    [property: JsonPropertyOrder(1)] string Title,
    [property: JsonPropertyOrder(2)] string? DueBy,
    [property: JsonPropertyOrder(3)] bool IsComplete);
