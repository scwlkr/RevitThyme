using RevitThyme.Lab;
using RevitThyme.Native;

if (args is ["--serve", var root]) { await TestServer.Run(root); return; }
string fixture = Path.Combine(Path.GetTempPath(), "RevitThyme-Test.rvt");
var guard = new FixtureGuard(fixture, false);
void Reject(Action action, string code)
{
    try { action(); throw new Exception("Expected rejection: " + code); }
    catch (Rejection e) when (e.Code == code) { Console.WriteLine("PASS " + code); }
}
Reject(() => guard.Check(Path.Combine(Path.GetTempPath(), "Working.rvt"), "job", false), "wrong_fixture");
Reject(() => guard.Check(fixture, "shared", true), "wrong_fixture");
guard.Check(fixture.ToUpperInvariant(), "first", false);
guard.Check(fixture, "first", false);
Reject(() => guard.Check(fixture, "reopened", false), "reopened_fixture");
Reject(guard.Write, "lab_read_only");
guard.Close();
Reject(() => guard.Check(fixture, "first", false), "reopened_fixture");
new FixtureGuard(fixture, true).Write();
Console.WriteLine("PASS approved native lab write guard");
