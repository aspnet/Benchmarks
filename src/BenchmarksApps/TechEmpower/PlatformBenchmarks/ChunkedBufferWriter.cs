using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PlatformBenchmarks;

internal sealed class ChunkedBufferWriter<TWriter> : IBufferWriter<byte> where TWriter : IBufferWriter<byte>
{
    private const int DefaultChunkSizeHint = 4096;
    private static readonly StandardFormat DefaultHexFormat = GetHexFormat(DefaultChunkSizeHint);

    private TWriter _output;
    private int _chunkSizeHint;
    private StandardFormat _hexFormat = DefaultHexFormat;
    private Memory<byte> _currentFullChunk;
    private Memory<byte> _currentChunk;
    private int _buffered;
    private bool _ended = false;

    public Memory<byte> Memory => _currentChunk;

    public TWriter Output => _output;

    public int Buffered => _buffered;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOutput(TWriter output, int chunkSizeHint = DefaultChunkSizeHint)
    {
        _buffered = 0;
        _chunkSizeHint = chunkSizeHint;
        _output = output;

        StartNewChunk(chunkSizeHint, isFirst: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _buffered = 0;
        _output = default;
        _ended = false;
        _hexFormat = DefaultHexFormat;
        _currentFullChunk = default;
        _currentChunk = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        ThrowIfEnded();

        _buffered += count;
        _currentChunk = _currentChunk[count..];
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfEnded();

        if (_currentChunk.Length <= sizeHint)
        {
            EnsureMore(sizeHint);
        }
        return _currentChunk;
    }

    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    public void End()
    {
        ThrowIfEnded();

        CommitCurrentChunk(isFinal: true);

        _ended = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StandardFormat GetHexFormat(int maxValue)
    {
        var hexDigitCount = CountHexDigits(maxValue);

        return new StandardFormat('X', (byte)hexDigitCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountHexDigits(int n) => n <= 16 ? 1 : (int)Math.Ceiling(Math.Log(n, 16));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StartNewChunk(int sizeHint, bool isFirst = false)
    {
        // Header is like:
        // 520\r\n

        var oldFullChunkHexLength = -1;
        if (!isFirst)
        {
            oldFullChunkHexLength = CountHexDigits(_currentFullChunk.Length);
        }
        _currentFullChunk = _output.GetMemory(Math.Max(_chunkSizeHint, sizeHint));
        var newFullChunkHexLength = CountHexDigits(_currentFullChunk.Length);

        var currentFullChunkSpan = _currentFullChunk.Span;

        // Write space for HEX digits
        currentFullChunkSpan[..newFullChunkHexLength].Fill(48); // 48 == '0'

        // Write header terminator
        var terminator = "\r\n"u8;
        terminator.CopyTo(currentFullChunkSpan[newFullChunkHexLength..]);
        var chunkHeaderLength = newFullChunkHexLength + terminator.Length;
        _currentChunk = _currentFullChunk[chunkHeaderLength..];

        if (!isFirst && oldFullChunkHexLength != newFullChunkHexLength)
        {
            // Update HEX format if changed
            _hexFormat = GetHexFormat(_currentFullChunk.Length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CommitCurrentChunk(bool isFinal = false, int sizeHint = 0)
    {
        ThrowIfEnded();

        var contentLength = _buffered;
        
        if (contentLength > 0)
        {
            // Update the chunk header
            var maxLength = CountHexDigits(contentLength);
            var span = _currentFullChunk.Span;
            if (!Utf8Formatter.TryFormat(contentLength, span, out var bytesWritten, _hexFormat))
            {
                throw new NotSupportedException("Chunk size too large");
            }
            Debug.Assert(maxLength == bytesWritten, "HEX formatting math problem.");
            var headerLength = maxLength + 2;

            // Total chunk length: content length as HEX string + \r\n + content + \r\n
            var spanOffset = headerLength + contentLength;
            var chunkTotalLength = spanOffset + 2;

            // Write out the chunk terminator
            "\r\n"u8.CopyTo(span[spanOffset..]);
            spanOffset = chunkTotalLength;
            
            if (!isFinal)
            {
                _output.Advance(chunkTotalLength);
                StartNewChunk(sizeHint);
            }
            else
            {
                // Write out final chunk (zero-length chunk)
                var terminator = "0\r\n\r\n"u8;
                if ((spanOffset + terminator.Length) <= span.Length)
                {
                    // There's space for the final chunk in the current span
                    terminator.CopyTo(span[spanOffset..]);
                    _output.Advance(chunkTotalLength + terminator.Length);
                }
                else
                {
                    // Final chunk doesn't fit in current span so just write it directly after advancing the writer
                    _output.Advance(chunkTotalLength);
                    _output.Write(terminator);
                }
            }
            
            _buffered = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> source)
    {
        ThrowIfEnded();

        if (_currentChunk.Length >= source.Length)
        {
            source.CopyTo(_currentChunk.Span);
            Advance(source.Length);
        }
        else
        {
            WriteMultiBuffer(source);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureMore(int count = 0)
    {
        if ((_buffered + count) > _currentChunk.Length)
        {
            if (_buffered > 0)
            {
                CommitCurrentChunk(isFinal: false, count);
            }
            else
            {
                StartNewChunk(count);
            }
        }
    }

    private void WriteMultiBuffer(ReadOnlySpan<byte> source)
    {
        while (source.Length > 0)
        {
            if (_currentChunk.Length == 0)
            {
                EnsureMore();
            }

            var writable = Math.Min(source.Length, _currentChunk.Length);
            source[..writable].CopyTo(_currentChunk.Span);
            source = source[writable..];
            Advance(writable);
        }
    }

    private void ThrowIfEnded()
    {
        if (_ended)
        {
            throw new InvalidOperationException("Cannot use the writer after calling End().");
        }
    }
}