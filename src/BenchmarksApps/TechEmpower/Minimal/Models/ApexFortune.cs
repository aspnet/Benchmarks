namespace Minimal.Models;

public readonly record struct ApexFortune(
    int Id,
    ReadOnlyMemory<byte> Message) : IComparable<ApexFortune>
{
    public int CompareTo(ApexFortune other) =>
        Message.Span.SequenceCompareTo(other.Message.Span);
}
