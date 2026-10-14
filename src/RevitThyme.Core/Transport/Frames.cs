using System.Buffers.Binary;
using System.Text.Json;

namespace RevitThyme.Core;

public static class Frames
{
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Protocol.Json);
        if (bytes.Length > Protocol.MaxBytes) throw new InvalidDataException("Frame too large.");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > Protocol.MaxBytes) throw new InvalidDataException("Invalid frame length.");
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes, Protocol.Json) ?? throw new InvalidDataException("Empty frame.");
    }
}
