using System;
using System.Buffers;

namespace PipeliningClient
{
    public static class SequenceReaderExtensions
    {
        public static bool TryReadTo<T>(this ref SequenceReader<T> sequenceReader, out ReadOnlySpan<T> span, ReadOnlySpan<T> delimiter, bool advancePastDelimiter = true) where T : unmanaged, IEquatable<T>
        {
            if (sequenceReader.TryReadTo(out ReadOnlySequence<T> sequence, delimiter, advancePastDelimiter))
            {
                span = sequence.IsSingleSegment ? sequence.FirstSpan : sequence.ToArray();
                return true;
            }
            span = default;
            return false;
        }
    }
}
