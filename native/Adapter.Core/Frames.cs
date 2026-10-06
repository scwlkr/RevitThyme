using System.Buffers.Binary;
using System.Text.Json;

namespace RevitThyme.Native;
public static class Frames
{
    public static byte[] Encode<T>(T value)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json);
        if (body.Length is < 1 or > Wire.MaxBytes) throw new InvalidDataException("Frame limit.");
        var frame = new byte[body.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length); body.CopyTo(frame, 4); return frame;
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is < 1 or > Wire.MaxBytes) throw new InvalidDataException("Frame limit.");
        var body = new byte[size]; await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<T>(body, Wire.Json) ?? throw new InvalidDataException("Null frame.");
    }
    public static Task Write<T>(Stream stream, T value, CancellationToken token) => stream.WriteAsync(Encode(value), token).AsTask();
}
