using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitThyme.Native;
using Range = RevitThyme.Native.Range;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
async Task Reject(byte[] bytes, string name)
{
    bool rejected = false;
    try { var r = await Frames.Read<Request>(new MemoryStream(bytes), CancellationToken.None); Wire.Validate(r); }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or EndOfStreamException) { rejected = true; }
    Check(rejected, name);
}
var target = new Target(1234, "638000000000000000", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "unique-view", 7);
var plane = new Plane("4294967301", "Level", 0, -0.1234567890123, false, false);
var request = new Request(1, Guid.NewGuid().ToString(), Operation.Validate, target, "owned-snapshot", new Range(plane, plane, plane, plane), 0, "", false);
var decoded = await Frames.Read<Request>(new MemoryStream(Frames.Encode(request)), CancellationToken.None); Wire.Validate(decoded);
Check(decoded == request, "Production frame preserves exact negative feet and 64-bit level ID");
await Reject(Frames.Encode(request with { Protocol = 2 }), "Protocol mismatch");
await Reject(Frames.Encode(request with { RequestId = "" }), "Missing request ID");
await Reject(Frames.Encode(request with { Range = null }), "Validate requires canonical range");
await Reject(Frames.Encode(request with { Target = null }), "Validate requires native target");
await Reject(Frames.Encode(request with { Operation = Operation.Apply }), "Apply requires explicit confirmation");
var oversized = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(oversized, 65537);
await Reject(oversized, "Oversized frame before body allocation");
await Reject(Frames.Encode(request)[..^1], "Incomplete body rejected");
await Reject(Frames.Encode(request with { ChunkIndex = 391 }), "Geometry chunk overflow rejected");
foreach (var change in new[] { "operation", "target", "range", "extra", "unknown_operation", "null_plane", "nonfinite" })
{
    var json = JsonNode.Parse(JsonSerializer.Serialize(request, Wire.Json))!.AsObject();
    if (change == "extra") json["execute_code"] = "refused";
    else if (change == "unknown_operation") json["operation"] = "execute_code";
    else if (change == "null_plane") json["range"]!["top"] = null;
    else if (change == "nonfinite") json["range"]!["cut"]!["offset_feet"] = "Infinity";
    else json.Remove(change);
    var payload = System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()); var frame = new byte[payload.Length + 4];
    BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length); payload.CopyTo(frame, 4);
    await Reject(frame, "Closed production DTO rejects " + change);
}
using var concatenated = new MemoryStream(Frames.Encode(request).Concat(Frames.Encode(request)).ToArray());
await Frames.Read<Request>(concatenated, CancellationToken.None);
Check(concatenated.Position == Frames.Encode(request).Length, "Framing consumes exactly one declared body");
Console.WriteLine($"Adapter contract: {passed} passed. Production DTO/reader; no Revit API executed.");
