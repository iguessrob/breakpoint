using System.Buffers.Binary;
using System.Text.Json;

namespace Breakpoint.Core;

public static class Protocol
{
    public const string LivePipe = "Breakpoint.Control.v1";
    public const int MaxBytes = 16_384;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (data.Length > MaxBytes) throw new InvalidDataException("Message too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxBytes) throw new InvalidDataException("Invalid message length.");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        return JsonSerializer.Deserialize<T>(data, Json) ?? throw new InvalidDataException("Invalid message.");
    }
}
