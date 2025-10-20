using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SpawnCamper.Core;

internal sealed class LogReader(Stream stream) : IDisposable {
    private byte[] _buffer = new byte[1024];

    public void Dispose() {
        stream.Dispose();
    }

    public async ValueTask<T> ReadAsync<T>(CancellationToken token) where T : unmanaged {
        int size;
        unsafe {
            size = sizeof(T);
        }
        await stream.ReadExactlyAsync(_buffer, 0, size, token);
        return MemoryMarshal.Read<T>(_buffer);
    }

    public async ValueTask VerifyTerminatorAsync(CancellationToken token) {
        var terminator = await ReadAsync<uint>(token);
        if (terminator != 0x012345678) {
            throw new InvalidDataException("Malformed message from the traced process, incorrect terminator found.");
        }
    }

    public async ValueTask<Encoding> ReadEncodingAsync(CancellationToken token) {
        var codePage = await ReadAsync<int>(token);
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage);
        encoding ??= Encoding.GetEncoding(codePage);
        if (encoding == null) {
            throw new FormatException($"Unknown encoding passed by client: {codePage}");
        }
        return encoding;
    }

    public async ValueTask<string?> ReadStringAsync(Encoding encoding, CancellationToken token) {
        var len = await ReadAsync<ulong>(token);
        if (len == unchecked((ulong) -1)) {
            return null;
        }

        if ((int) len > _buffer.LongLength) {
            _buffer = new byte[len];
        }

        await stream.ReadExactlyAsync(_buffer, 0, (int) len, token);
        return encoding.GetString(_buffer, 0, (int) len);
    }

    public async ValueTask<Dictionary<string, string>> ReadEnvironmentBlockAsync(
            Encoding encoding, CancellationToken token) {
        // this is a horrible hack, but doing this properly is even more horrible (I tried for ~2 hours and mostly failed)
        // we decode the whole buffer, including the null terminators, and hope that the encoding leaves them alone
        var str = await ReadStringAsync(encoding, token);

        var result = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        var rest = str.AsSpan();
        while (!rest.IsEmpty) {
            var eqI = rest.IndexOf('=');
            var key = rest[..eqI].ToString();
            rest = rest[(eqI + 1)..];

            var endI = rest.IndexOf((char) 0);
            if (endI == -1) {
                throw new FormatException("Invalid environment block, last value does not have a null terminator.");
            }
            var value = rest[..endI].ToString();
            rest = rest[(endI + 1)..];

            if (key == "") {
                // special env vars like `=::=::\` and `=D:=...`, used by cmd.exe to track per-drive working directories
                // ignore them, the format is weird and, e.g., .NET also ignores them
                continue;
            }
            result.Add(key, value);
        }
        return result;
    }
}