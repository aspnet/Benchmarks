using System.Buffers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace NetworkingTransports;

// The record-protection engine behind TlsSessionDuplexPipe. Both implementations follow the sans-IO contract of the runtime's
// TlsBufferSession (dotnet/runtime#128871): the caller owns every byte of I/O, the session only transforms spans and reports
// how much it consumed and produced. Using one pipe over both engines means the comparison below differs only in the engine.
internal interface ITlsRecordSession : IDisposable
{
    bool IsHandshakeComplete { get; }

    bool HasPendingOutput { get; }

    TlsOperationStatus Handshake(ReadOnlySpan<byte> source, Span<byte> destination, out int consumed, out int written);

    TlsOperationStatus Read(ReadOnlySpan<byte> source, Span<byte> destination, out int consumed, out int written);

    TlsOperationStatus Write(ReadOnlySpan<byte> plaintext, Span<byte> destination, out int consumed, out int written);

    TlsOperationStatus DrainPendingOutput(Span<byte> destination, out int written);

    TlsOperationStatus Shutdown(Span<byte> destination, out int written);

    void AcceptPeerCertificate();
}

// The runtime's TlsBufferSession (SslStream's TLS engine without the Stream): OpenSSL on Linux, Schannel on Windows.
internal sealed class TlsBufferRecordSession : ITlsRecordSession
{
    private readonly TlsBufferSession _session = new();

    public TlsBufferRecordSession(TlsContext context) => _session.SetContext(context);

    public bool IsHandshakeComplete => _session.IsHandshakeComplete;

    public bool HasPendingOutput => _session.HasPendingOutput;

    public TlsOperationStatus Handshake(ReadOnlySpan<byte> source, Span<byte> destination, out int consumed, out int written)
        => _session.Handshake(source, destination, out consumed, out written);

    public TlsOperationStatus Read(ReadOnlySpan<byte> source, Span<byte> destination, out int consumed, out int written)
        => _session.Read(source, destination, out consumed, out written);

    public TlsOperationStatus Write(ReadOnlySpan<byte> plaintext, Span<byte> destination, out int consumed, out int written)
        => _session.Write(plaintext, destination, out consumed, out written);

    public TlsOperationStatus DrainPendingOutput(Span<byte> destination, out int written)
        => _session.DrainPendingOutput(destination, out written);

    public TlsOperationStatus Shutdown(Span<byte> destination, out int written)
        => _session.Shutdown(destination, out written);

    public void AcceptPeerCertificate() => _session.SetRemoteCertificateValidationResult(SslPolicyErrors.None);

    public void Dispose() => _session.Dispose();
}

// Schannel driven directly through secur32 (AcceptSecurityContext/DecryptMessage/EncryptMessage), with no SslStream or
// TlsBufferSession in between. It decrypts one record per Read call: DecryptMessage works in place, so the record is first
// copied to a per-session scratch buffer. That copy is the price of layering Schannel over a transport that hands out
// immutable spans; a transport-owned Schannel session decrypts in the buffer it received into.
internal sealed unsafe class SchannelRecordSession : ITlsRecordSession
{
    private const int RecordHeaderSize = 5;
    private const int MaxPlaintextRecord = 16 * 1024;
    private const int MaxCipherRecord = MaxPlaintextRecord + 2048 + RecordHeaderSize;

    private readonly SchannelNative.Credential _credential;
    private readonly SchannelNative.SecurityHandle _context = new(credential: false);
    private SchannelNative.StreamSizes _sizes;
    private byte[]? _scratch;
    private byte[]? _pending;
    private int _pendingOffset;
    private int _pendingLength;
    private bool _complete;
    private bool _peerClosed;

    public SchannelRecordSession(SchannelNative.Credential credential) => _credential = credential;

    public bool IsHandshakeComplete => _complete;

    public bool HasPendingOutput => _pendingLength > _pendingOffset;

    public TlsOperationStatus Handshake(ReadOnlySpan<byte> source, Span<byte> destination, out int consumed, out int written)
    {
        consumed = 0;
        written = 0;
        if (_complete)
        {
            return TlsOperationStatus.Complete;
        }
        if (source.IsEmpty)
        {
            return TlsOperationStatus.NeedMoreData;
        }

        var status = SchannelNative.Handshake(_credential, _context, source, out var extra, out var token, out var tokenLength);
        try
        {
            if (status == SchannelNative.Incomplete)
            {
                return TlsOperationStatus.NeedMoreData;
            }
            if (status is not (0 or SchannelNative.Continue))
            {
                SchannelNative.Check(status, "AcceptSecurityContext");
            }

            consumed = source.Length - extra;
            if (tokenLength > 0)
            {
                var output = new ReadOnlySpan<byte>((void*)token, tokenLength);
                if (!HasPendingOutput && output.Length <= destination.Length)
                {
                    output.CopyTo(destination);
                    written = output.Length;
                }
                else
                {
                    AppendPending(output);
                }
            }
        }
        finally
        {
            SchannelNative.FreeToken(token);
        }

        if (status == 0)
        {
            SchannelNative.GetInfo(_context, out _sizes);
            _complete = true;
            return TlsOperationStatus.Complete;
        }
        return consumed > 0 || tokenLength > 0 ? TlsOperationStatus.Complete : TlsOperationStatus.NeedMoreData;
    }

    public TlsOperationStatus Read(ReadOnlySpan<byte> source, Span<byte> destination, out int consumed, out int written)
    {
        consumed = 0;
        written = 0;
        if (_peerClosed)
        {
            return TlsOperationStatus.Closed;
        }
        if (source.Length < RecordHeaderSize)
        {
            return TlsOperationStatus.NeedMoreData;
        }

        var payload = (source[3] << 8) | source[4];
        var frame = RecordHeaderSize + payload;
        if (frame > MaxCipherRecord)
        {
            throw new IOException("The peer sent an oversized TLS record.");
        }
        if (source.Length < frame)
        {
            return TlsOperationStatus.NeedMoreData;
        }
        // The plaintext of a record is never larger than its payload, and never larger than one plaintext record.
        if (destination.Length < Math.Min(payload, MaxPlaintextRecord))
        {
            return TlsOperationStatus.DestinationTooSmall;
        }

        var scratch = _scratch ??= ArrayPool<byte>.Shared.Rent(MaxCipherRecord);
        source[..frame].CopyTo(scratch);
        var status = SchannelNative.Decrypt(_context, scratch.AsSpan(0, frame), out var offset, out var length, out var extra);
        if (status == SchannelNative.Incomplete)
        {
            return TlsOperationStatus.NeedMoreData;
        }
        if (status == SchannelNative.Renegotiate)
        {
            throw new AuthenticationException("Post-handshake TLS renegotiation/key-update is not supported by this benchmark layer.");
        }
        if (status is not (0 or SchannelNative.Expired))
        {
            SchannelNative.Check(status, "DecryptMessage");
        }

        consumed = frame - extra;
        if (status == SchannelNative.Expired)
        {
            _peerClosed = true;
            return TlsOperationStatus.Closed;
        }

        scratch.AsSpan(offset, length).CopyTo(destination);
        written = length;
        return TlsOperationStatus.Complete;
    }

    public TlsOperationStatus Write(ReadOnlySpan<byte> plaintext, Span<byte> destination, out int consumed, out int written)
    {
        consumed = 0;
        written = 0;
        if (plaintext.IsEmpty)
        {
            return TlsOperationStatus.Complete;
        }

        var overhead = checked((int)(_sizes.Header + _sizes.Trailer));
        var count = Math.Min(plaintext.Length, (int)_sizes.MaximumMessage);
        // One whole record or nothing: a short tail of the output segment must not split the response into tiny records.
        if (destination.Length < overhead + count)
        {
            return TlsOperationStatus.DestinationTooSmall;
        }

        written = SchannelNative.Encrypt(_context, _sizes, plaintext[..count], destination);
        consumed = count;
        return TlsOperationStatus.Complete;
    }

    public TlsOperationStatus DrainPendingOutput(Span<byte> destination, out int written)
    {
        written = 0;
        if (!HasPendingOutput)
        {
            return TlsOperationStatus.Complete;
        }

        var count = Math.Min(destination.Length, _pendingLength - _pendingOffset);
        _pending.AsSpan(_pendingOffset, count).CopyTo(destination);
        _pendingOffset += count;
        written = count;
        if (HasPendingOutput)
        {
            return TlsOperationStatus.DestinationTooSmall;
        }

        _pendingOffset = 0;
        _pendingLength = 0;
        return TlsOperationStatus.Complete;
    }

    public TlsOperationStatus Shutdown(Span<byte> destination, out int written)
    {
        written = 0;
        SchannelNative.BeginShutdown(_context);
        var status = SchannelNative.Handshake(_credential, _context, default, out _, out var token, out var length);
        try
        {
            if (status is not (0 or SchannelNative.Expired))
            {
                SchannelNative.Check(status, "AcceptSecurityContext shutdown");
            }
            if (length > destination.Length)
            {
                return TlsOperationStatus.DestinationTooSmall;
            }
            new ReadOnlySpan<byte>((void*)token, length).CopyTo(destination);
            written = length;
            return TlsOperationStatus.Complete;
        }
        finally
        {
            SchannelNative.FreeToken(token);
        }
    }

    public void AcceptPeerCertificate()
    {
    }

    public void Dispose()
    {
        _context.Dispose();
        if (_scratch is not null)
        {
            ArrayPool<byte>.Shared.Return(_scratch);
            _scratch = null;
        }
        if (_pending is not null)
        {
            ArrayPool<byte>.Shared.Return(_pending);
            _pending = null;
        }
    }

    private void AppendPending(ReadOnlySpan<byte> data)
    {
        var needed = _pendingLength + data.Length;
        if (_pending is null || _pending.Length < needed)
        {
            var next = ArrayPool<byte>.Shared.Rent(Math.Max(needed, 4096));
            if (_pending is not null)
            {
                _pending.AsSpan(0, _pendingLength).CopyTo(next);
                ArrayPool<byte>.Shared.Return(_pending);
            }
            _pending = next;
        }
        data.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength = needed;
    }
}
