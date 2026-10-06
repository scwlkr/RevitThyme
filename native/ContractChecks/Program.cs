using System.Buffers.Binary;
using System.Text;
using RevitThyme.ContractChecks;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; } catch (System.Text.Json.JsonException) { rejected = true; }
    Check(rejected, name);
}
var target = new NativeTarget(1234, 638000000000000000, Guid.NewGuid(), Guid.NewGuid(), "unique-view", 7);
var plane = new NativePlane("4294967301", -0.1234567890123, false);
var range = new NativeRange(plane, plane, plane, plane);
var request = new NativeRequest(1, Guid.NewGuid(), Operation.Validate, target, "owned-snapshot", range);
Check(Protocol.Read(Protocol.Frame(request), target) == request, "Exact negative feet and 64-bit level ID round trip");
foreach (var mismatch in new[] {target with {ProcessId=5678},target with {ProcessStartTicks=2},target with {SessionId=Guid.NewGuid()},target with {DocumentId=Guid.NewGuid()},target with {ViewUniqueId="other"},target with {Revision=8}})
    Reject(() => Protocol.Read(Protocol.Frame(request), mismatch), "Wrong native target rejected");
Reject(() => Protocol.Read(Protocol.Frame(request with {Protocol=2}), target), "Protocol mismatch");
Reject(() => Protocol.Read(Protocol.Frame(request with {RequestId=Guid.Empty}), target), "Missing request ID");
Reject(() => Protocol.Read(Protocol.Frame(request with {Range=null}), target), "Validate requires canonical range");
var oversized = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(oversized, 65537);
Reject(() => Protocol.Read(oversized, target), "Oversized frame rejected before body allocation");
var trailing=Protocol.Frame(request).Concat(new byte[]{0}).ToArray();
Reject(() => Protocol.Read(trailing,target),"Trailing bytes rejected");
var json=System.Text.Json.JsonSerializer.Serialize(request,Protocol.Json).Replace("\"operation\":\"validate\"","\"operation\":\"executeCode\"");
var body=Encoding.UTF8.GetBytes(json);var frame=new byte[body.Length+4];BinaryPrimitives.WriteInt32LittleEndian(frame,body.Length);body.CopyTo(frame,4);
Reject(()=>Protocol.Read(frame,target),"Arbitrary method rejected");
foreach (var field in new[] { "operation", "target", "range" })
{
    var malformed = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(request, Protocol.Json))!.AsObject();
    if (field == "range") malformed["range"]!["top"] = null;
    else malformed.Remove(field);
    var payload = Encoding.UTF8.GetBytes(malformed.ToJsonString());
    var malformedFrame = new byte[payload.Length + 4];
    BinaryPrimitives.WriteInt32LittleEndian(malformedFrame, payload.Length);
    payload.CopyTo(malformedFrame, 4);
    Reject(() => Protocol.Read(malformedFrame, target), "Incomplete or null native frame rejected: " + field);
}
Console.WriteLine($"Adapter contract: {passed} passed. No Revit API executed.");
