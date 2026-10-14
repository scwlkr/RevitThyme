using System.Text.Json;
using RevitThyme.Core;
using RevitThyme.Execution;

namespace RevitThyme.Unit;

internal static class Dynamic
{
    public static async Task Run(Action<string, bool> check)
    {
        var model = new Model(); var executor = new OperationExecutor();
        OperationRequest Request(string source = "return 42;") => new(Guid.NewGuid(), OperationKind.RunQuery, model.Capture().Target, Code: new(source, true));
        check("code requires host opt-in", executor.Execute(model, Request()).Code == "trusted_code_disabled" && model.Calls == 0);
        model.TrustedCodeEnabled = true;
        check("code requires caller trust", executor.Execute(model, Request() with { Code = new("return 42;") }).Code == "trusted_code_disabled");
        check("code stale target rejected before execution", executor.Execute(model, Request() with { Target = model.Capture().Target with { Revision = 99 } }).Code == "stale_target");
        check("code source byte bound handles Unicode", executor.Execute(model, Request(new string('\u2603', 10000))).Code == "invalid_code");
        check("code empty source rejected", executor.Execute(model, Request(" ")).Code == "invalid_code");
        check("code budget bounds checked", executor.Execute(model, Request() with { Code = new("return 1;", true, 30001) }).Code == "invalid_code");
        check("code cannot carry utility arguments", executor.Execute(model, Request() with { RoomOptions = new() }).Code == "invalid_request");
        check("code cannot be smuggled into other operations", executor.Execute(model, Request() with { Operation = OperationKind.Inspect, Target = null }).Code == "invalid_request");
        var queue = new OperationDispatcher(); var request = Request(); var pending = queue.ExecuteAsync(request);
        var copy = JsonSerializer.Deserialize<OperationRequest>(JsonSerializer.Serialize(request, Protocol.Json), Protocol.Json)!;
        check("dynamic replay compares source and trust by value", queue.ExecuteAsync(copy) == pending);
        queue.ExecuteNext(model); await pending;
        check("dynamic retained request executes once", (await queue.ExecuteAsync(copy)).Succeeded && model.Calls == 1);
        check("changed source with same request ID conflicts", (await queue.ExecuteAsync(copy with { Code = new("return 43;", true) })).Code == "id_conflict");
        using var cancelled = new CancellationTokenSource(); var queued = queue.ExecuteAsync(Request(), cancelled.Token); cancelled.Cancel(); queue.ExecuteNext(model);
        check("cancelled dynamic code never executes", (await queued).Code == "cancelled" && model.Calls == 1);
        var valid = CSharpCompiler.Compile("return new { answer = value + 1 };", "int value", "using System;", []);
        using (valid.Code)
        {
            if (valid.Code is null) throw new InvalidOperationException(JsonSerializer.Serialize(valid.Diagnostics));
            check("real Roslyn executes compiled code and serializes anonymous result", JsonSerializer.Serialize(valid.Code.Run(41)).Contains("42"));
        }
        var invalid = CSharpCompiler.Compile("int x = ;\nreturn x;", "", "", []);
        check("compiler diagnostics refer to submission line", invalid.Code is null && invalid.Diagnostics.Any(d => d.Line == 1 && d.Severity == "Error"));
        var failure = CSharpCompiler.Compile("throw new InvalidOperationException(\"expected\");", "", "using System;", []);
        using (failure.Code)
        {
            bool unwrapped = false; try { failure.Code!.Run(); } catch (InvalidOperationException e) { unwrapped = e.Message == "expected"; }
            check("runtime exception unwrapped for native rollback", unwrapped);
        }
        var asyncCode = CSharpCompiler.Compile("await System.Threading.Tasks.Task.Delay(1); return null;", "", "", []);
        check("async code cannot escape synchronous API callback", asyncCode.Code is null);
    }
    private sealed class Model : IDynamicModel
    {
        private readonly FakeModel model = new();
        public bool TrustedCodeEnabled { get; set; }
        public int Calls { get; private set; }
        public Snapshot Capture() => model.Capture();
        public string ReadViewName() => model.ReadViewName();
        public IChange BeginChange() => model.BeginChange();
        public OperationResult ExecuteCode(OperationRequest request) { Calls++; return new(request.Id, Outcome.Inspected, "inspected", "test"); }
    }
}
