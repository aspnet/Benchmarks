namespace ApexFortunes;

public readonly struct Fortune : IComparable<Fortune>
{
    public Fortune(int id, ReadOnlyMemory<byte> message)
    {
        Id = id;
        Message = message;
    }

    public int Id { get; }

    public ReadOnlyMemory<byte> Message { get; }

    public int CompareTo(Fortune other) =>
        Message.Span.SequenceCompareTo(other.Message.Span);
}
