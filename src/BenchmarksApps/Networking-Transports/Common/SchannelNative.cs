using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace NetworkingTransports;

// Adapted from src/Servers/Kestrel/Transport.Networking/src/Windows/Schannel.cs of the prototype (the secur32 layer the
// transport-owned Windows TLS uses), so both arms drive Schannel through the same SSPI calls. The changes are span-based
// signatures and a credential built from an X509Certificate2 instead of the transport's NetworkNativeTlsOptions.
// Layouts follow Windows SDK 10.0.26100 sspi.h/schannel.h. SECURITY_STATUS is a signed Win32 LONG.
internal static unsafe partial class SchannelNative
{
    internal const int Continue = 0x90312, Expired = 0x90317, Renegotiate = 0x90321;
    internal const int Incomplete = unchecked((int)0x80090318);
    internal const uint Data = 1, Token = 2, Extra = 5, Trailer = 6, Header = 7;
    private const uint Requirements = 0x00010000 | 0x00000100 | 0x00000010 | 0x00000008 | 0x00000004;

    // TLS 1.3 only by default (SP_PROT_TLS1_3_SERVER). NETWORKPROTO_WINDOWS_TLS=1.2 selects TLS 1.2 only (SP_PROT_TLS1_2_SERVER).
    internal static readonly bool UseTls12 = Environment.GetEnvironmentVariable("NETWORKPROTO_WINDOWS_TLS") == "1.2";
    internal static uint ServerProtocol => UseTls12 ? 0x00000400u : 0x00001000u;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Buffer
    {
        internal uint Length, Type;
        internal void* Pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferList
    {
        internal uint Version, Count;
        internal Buffer* Buffers;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credentials
    {
        internal uint Version, Format, CertificateCount;
        internal nint* Certificates;
        internal nint RootStore;
        internal uint MapperCount;
        internal nint Mappers;
        internal uint SessionLifespan, Flags, ParameterCount;
        internal TlsParameters* Parameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TlsParameters
    {
        internal uint AlpnCount;
        internal nint Alpn;
        internal uint DisabledProtocols, DisabledCryptoCount;
        internal nint DisabledCrypto;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamSizes
    {
        internal uint Header, Trailer, MaximumMessage, Buffers, BlockSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ConnectionInfo
    {
        internal uint Protocol, Cipher, CipherStrength, Hash, HashStrength, Exchange, ExchangeStrength;
    }

    internal sealed class Credential : IDisposable
    {
        internal SecurityHandle Handle { get; }

        // The certificate must carry a persisted key (a PFX re-import), which Schannel cannot get from a PEM-created key.
        internal Credential(X509Certificate2 certificate)
        {
            if (sizeof(Buffer) != 16 || sizeof(BufferList) != 16 || sizeof(Credentials) != 72
                || sizeof(TlsParameters) != 40 || sizeof(StreamSizes) != 20 || sizeof(ConnectionInfo) != 28)
            {
                throw new PlatformNotSupportedException("The Schannel layer requires the Windows x64 SSPI layout.");
            }
            Handle = new SecurityHandle(credential: true);
            try
            {
                var handle = certificate.Handle;
                var parameters = new TlsParameters { DisabledProtocols = ~ServerProtocol };
                var credentials = new Credentials
                {
                    Version = 5,
                    CertificateCount = 1,
                    Certificates = &handle,
                    Flags = 0x00000080 | 0x00400000,
                    ParameterCount = 1,
                    Parameters = &parameters
                };
                Check(AcquireCredentialsHandleW(0, "Schannel", 1, 0, &credentials, 0, 0, Handle, out _), "AcquireCredentialsHandleW");
                Handle.Acquired = true;
            }
            catch
            {
                Handle.Dispose();
                throw;
            }
        }

        public void Dispose() => Handle.Dispose();
    }

    internal sealed class SecurityHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly bool _credential;
        internal bool Acquired { get; set; }
        internal SecurityHandle(bool credential) : base(ownsHandle: true)
        {
            _credential = credential;
            var memory = NativeMemory.AllocZeroed((nuint)(2 * sizeof(nuint)));
            if ((nint)memory == 0)
            {
                throw new InvalidOperationException("The native SChannel handle allocation failed.");
            }
            SetHandle((nint)memory);
        }
        protected override bool ReleaseHandle()
        {
            var result = !Acquired ? 0 : _credential ? FreeCredentialsHandle(handle) : DeleteSecurityContext(handle);
            NativeMemory.Free((void*)handle);
            return result == 0;
        }
    }

    internal static int Handshake(Credential credential, SecurityHandle context, ReadOnlySpan<byte> input,
        out int extra, out nint output, out int outputLength)
    {
        fixed (byte* pointer = input)
        {
            var buffers = stackalloc Buffer[2];
            buffers[0] = new Buffer { Type = Token, Pointer = pointer, Length = (uint)input.Length };
            buffers[1] = default;
            var source = new BufferList { Count = 2, Buffers = buffers };
            var token = new Buffer { Type = Token };
            var destination = new BufferList { Count = 1, Buffers = &token };
            var result = AcceptSecurityContext(credential.Handle, context.Acquired ? context.DangerousGetHandle() : 0,
                input.IsEmpty ? null : &source, Requirements, 0, context, &destination, out _, out _);
            if (result is 0 or Continue)
            {
                context.Acquired = true;
            }
            extra = buffers[1].Type == Extra ? checked((int)buffers[1].Length) : 0;
            output = (nint)token.Pointer;
            outputLength = checked((int)token.Length);
            return result;
        }
    }

    internal static void GetInfo(SecurityHandle context, out StreamSizes sizes)
    {
        StreamSizes stream = default;
        ConnectionInfo connection = default;
        Check(QueryContextAttributesW(context, 4, &stream), "SECPKG_ATTR_STREAM_SIZES");
        Check(QueryContextAttributesW(context, 0x5a, &connection), "SECPKG_ATTR_CONNECTION_INFO");
        if (connection.Protocol != ServerProtocol || stream.MaximumMessage is 0 or > 16384 || stream.Header > 1024 || stream.Trailer > 1024)
        {
            throw new AuthenticationException("SChannel negotiated an unexpected protocol or record layout.");
        }
        sizes = stream;
    }

    // Decrypts in place: the caller passes a mutable copy of exactly one record.
    internal static int Decrypt(SecurityHandle context, Span<byte> input, out int offset, out int length, out int extra)
    {
        fixed (byte* pointer = input)
        {
            var buffers = stackalloc Buffer[4];
            for (var index = 0; index < 4; index++)
            {
                buffers[index] = default;
            }
            buffers[0] = new Buffer { Type = Data, Pointer = pointer, Length = (uint)input.Length };
            var list = new BufferList { Count = 4, Buffers = buffers };
            var result = DecryptMessage(context, &list, 0, out _);
            offset = length = extra = 0;
            if (result is 0 or Expired or Renegotiate)
            {
                for (var index = 0; index < 4; index++)
                {
                    if (result == 0 && buffers[index].Type == Data)
                    {
                        var start = (byte*)buffers[index].Pointer - pointer;
                        var count = checked((int)buffers[index].Length);
                        if (start < 0 || start > input.Length || count > input.Length - start)
                        {
                            throw new IOException("SChannel returned a plaintext range outside its owned record.");
                        }
                        offset = (int)start;
                        length = count;
                    }
                    else if (buffers[index].Type == Extra)
                    {
                        extra = checked((int)buffers[index].Length);
                    }
                }
            }
            return result;
        }
    }

    internal static int Encrypt(SecurityHandle context, StreamSizes sizes, ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        var header = checked((int)sizes.Header);
        var trailer = checked((int)sizes.Trailer);
        plaintext.CopyTo(destination[header..]);
        fixed (byte* start = destination)
        {
            var buffers = stackalloc Buffer[4];
            buffers[0] = new Buffer { Type = Header, Pointer = start, Length = sizes.Header };
            buffers[1] = new Buffer { Type = Data, Pointer = start + header, Length = (uint)plaintext.Length };
            buffers[2] = new Buffer { Type = Trailer, Pointer = start + header + plaintext.Length, Length = (uint)trailer };
            buffers[3] = default;
            var list = new BufferList { Count = 4, Buffers = buffers };
            Check(EncryptMessage(context, 0, &list, 0), "EncryptMessage");
            var written = 0;
            for (var index = 0; index < 3; index++)
            {
                var count = checked((int)buffers[index].Length);
                new ReadOnlySpan<byte>(buffers[index].Pointer, count).CopyTo(destination[written..]);
                written += count;
            }
            return written;
        }
    }

    internal static void BeginShutdown(SecurityHandle context)
    {
        uint shutdown = 1;
        var token = new Buffer { Type = Token, Pointer = &shutdown, Length = sizeof(uint) };
        var list = new BufferList { Count = 1, Buffers = &token };
        Check(ApplyControlToken(context, &list), "SCHANNEL_SHUTDOWN");
    }

    internal static void FreeToken(nint token)
    {
        if (token != 0)
        {
            Check(FreeContextBuffer(token), "FreeContextBuffer");
        }
    }

    internal static void Check(int status, string operation)
    {
        if (status != 0)
        {
            throw new AuthenticationException($"{operation} failed with SSPI status 0x{status:x8}.", new Win32Exception(status));
        }
    }

    [LibraryImport("secur32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AcquireCredentialsHandleW(nint principal, string package, uint use, nint logon,
        Credentials* credentials, nint getKey, nint argument, SecurityHandle handle, out long expiry);
    [LibraryImport("secur32.dll")]
    private static partial int AcceptSecurityContext(SecurityHandle credential, nint existing, BufferList* input,
        uint requirements, uint representation, SecurityHandle context, BufferList* output, out uint attributes, out long expiry);
    [LibraryImport("secur32.dll")]
    private static partial int QueryContextAttributesW(SecurityHandle context, uint attribute, void* result);
    [LibraryImport("secur32.dll")]
    private static partial int EncryptMessage(SecurityHandle context, uint quality, BufferList* message, uint sequence);
    [LibraryImport("secur32.dll")]
    private static partial int DecryptMessage(SecurityHandle context, BufferList* message, uint sequence, out uint quality);
    [LibraryImport("secur32.dll")]
    private static partial int ApplyControlToken(SecurityHandle context, BufferList* token);
    [LibraryImport("secur32.dll")]
    private static partial int FreeContextBuffer(nint token);
    [LibraryImport("secur32.dll")]
    private static partial int FreeCredentialsHandle(nint credential);
    [LibraryImport("secur32.dll")]
    private static partial int DeleteSecurityContext(nint context);
}
