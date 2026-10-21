using System.Text.Json;
using ModelContextProtocol.Client;

namespace RevitThyme.McpTests;

internal sealed class ResultCheckLive(ResultCheckFixture fixture)
{
    private string originalName = "", originalNumber = "";
    private long viewId, projectId;
    public static Task Run(string folder) => new ResultCheckLive(new(folder, "native")).Qualify();
    private static Dictionary<string, object?> Check(string name, string source, string comparison, object? expected = null) => new()
    { ["name"] = name, ["source"] = source, ["comparison"] = comparison, ["expected"] = expected };
    private static object Plan(object[] checks, string? allowed = null) => new
    {
        checks, allowedChangesSource = allowed ?? "return new { modified = System.Array.Empty<long>(), deleted = System.Array.Empty<long>() };",
        maxAdded = 0, addedCategoryIds = Array.Empty<long>()
    };
    private object NamePlan(string name, string? observer = null) => Plan(
        [Check("native view name", observer ?? "return new { exists = true, value = doc.ActiveView.Name };", "equals", name)],
        "return new { modified = new long[] { doc.ActiveView.Id.Value }, deleted = System.Array.Empty<long>() };");
    private async Task<JsonElement> Save(McpClient client, string source, object plan, string name, string mode = "modify")
    {
        var definition = new
        {
            name, request = "Qualify native workflow result checks in an owned disposable fixture", description = name, mode, source,
            inputs = new[] { new { name = "name", type = "string", description = "Expected native view name", required = true } },
            expectedResult = "Declared result checks match native model state and allowed changes",
            tests = new[] { new { name = "native assertion", inputs = new { name = originalName }, assumptions = "Copied pinned development model with native active view",
                expectedResult = "Native active view name matches input", assertionSource = "return doc.ActiveView.Name == inputs.GetProperty(\"name\").GetString();" } },
            resultChecks = plan
        };
        return await fixture.Call(client, "save_tool", new() { ["toolId"] = Guid.NewGuid().ToString(), ["definition"] = definition });
    }
    private async Task<Dictionary<string, object?>> Args(McpClient client, JsonElement saved, string name)
    {
        var context = await fixture.Context(client);
        return new()
        {
            ["toolId"] = saved.GetProperty("toolId").GetString(), ["digest"] = saved.GetProperty("digest").GetString(),
            ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(),
            ["inputs"] = new { name }, ["trusted"] = true
        };
    }
    private async Task<JsonElement> Run(McpClient client, Dictionary<string, object?> args, string status)
    {
        var result = await fixture.Call(client, "run_tool", args, null);
        fixture.Check("automatic result verification is " + status, result.GetProperty("verification").GetProperty("status").GetString() == status);
        return result;
    }
    private async Task<JsonElement> Modify(McpClient client, string source)
    {
        var context = await fixture.Context(client);
        return await fixture.Call(client, "run_modify", new()
        {
            ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(), ["source"] = source, ["trusted"] = true
        });
    }
    private async Task Qualify()
    {
        try
        {
            await using var client = await fixture.Client();
            var state = await fixture.Query(client, "return new { name = doc.ActiveView.Name, number = doc.ProjectInformation.Number, viewId = doc.ActiveView.Id.Value, projectId = doc.ProjectInformation.Id.Value };");
            originalName = state.GetProperty("name").GetString()!; originalNumber = state.GetProperty("number").GetString()!;
            viewId = state.GetProperty("viewId").GetInt64(); projectId = state.GetProperty("projectId").GetInt64();
            var inputCheck = Check("native name from input", "return new { exists = true, value = doc.ActiveView.Name };", "equals");
            inputCheck.Remove("expected"); inputCheck["expectedInput"] = "name";
            var range = Check("native element count", "return new { exists = true, value = new FilteredElementCollector(doc).GetElementCount() };", "range");
            range["minimum"] = 1; range["maximum"] = 1000000000;
            var queryPlan = Plan([inputCheck,
                Check("active view exists", "return new { exists = doc.ActiveView != null, value = (object)null };", "exists"),
                Check("missing object is absent", "return new { exists = doc.GetElement(new ElementId(9223372036854770000L)) != null, value = (object)null };", "absent"), range]);
            var query = await Save(client, "return new { success = true };", queryPlan, "Checked native query", "query");
            var wrongDigest = await Args(client, query, originalName); wrongDigest["digest"] = new string('0', 64);
            await fixture.Call(client, "run_tool", wrongDigest, true);
            fixture.Check("changed saved-tool digest cannot submit the intended action", (await fixture.Query(client, "return doc.ActiveView.Name;")).GetString() == originalName);
            var queryResult = await Run(client, await Args(client, query, originalName), "passed");
            fixture.Check("checked query preserves original inspected outcome", queryResult.GetProperty("outcome").GetString() == "Inspected");
            fixture.Check("native equals-input, exists, absent and range comparisons all pass", queryResult.GetProperty("verification").GetProperty("checks").GetArrayLength() == 4
                && queryResult.GetProperty("verification").GetProperty("checks").EnumerateArray().All(c => c.GetProperty("status").GetString() == "passed"));
            var afterQuery = await fixture.Context(client);
            fixture.Check("pure result observations do not silently advance native revision", afterQuery.GetProperty("result").GetProperty("snapshot").GetProperty("target").GetProperty("revision").GetInt64()
                == queryResult.GetProperty("snapshot").GetProperty("target").GetProperty("revision").GetInt64());

            string changedName = "Result Checked " + Guid.NewGuid().ToString("N")[..8];
            var intended = await Save(client, "doc.ActiveView.Name = inputs.GetProperty(\"name\").GetString(); return new { success = true };", NamePlan(changedName), "Checked view rename");
            var intendedArgs = await Args(client, intended, changedName); var intendedResult = await Run(client, intendedArgs, "passed");
            fixture.Check("checked modification preserves actual native outcome and changed IDs", intendedResult.GetProperty("outcome").GetString() == "Applied"
                && intendedResult.GetProperty("execution").GetProperty("changes").GetProperty("modified").EnumerateArray().Any(v => v.GetInt64() == viewId));
            var replay = await Run(client, intendedArgs, "passed");
            fixture.Check("exact checked request replay retains original write identity", replay.GetProperty("id").GetString() == intendedResult.GetProperty("id").GetString());
            var recovered = await fixture.Call(client, "get_operation", new() { ["requestId"] = intendedArgs["requestId"] });
            fixture.Check("native recovery retains original write separately from observer", recovered.GetProperty("id").GetString() == intendedResult.GetProperty("id").GetString()
                && recovered.GetProperty("execution").GetProperty("sourceSha256").GetString() == intendedResult.GetProperty("execution").GetProperty("sourceSha256").GetString());
            await fixture.Undo(client, originalName, originalNumber);

            string absentName = "Never Applied " + Guid.NewGuid().ToString("N")[..8];
            var claimed = await Save(client, "return new { success = true };", NamePlan(absentName), "False success source");
            var falseSuccess = await Run(client, await Args(client, claimed, absentName), "failed");
            fixture.Check("source success is not native result proof", falseSuccess.GetProperty("outcome").GetString() == "Applied"
                && (await fixture.Query(client, "return doc.ActiveView.Name;")).GetString() == originalName);

            changedName = "Result Extra " + Guid.NewGuid().ToString("N")[..8];
            var extra = await Save(client, "doc.ActiveView.Name = inputs.GetProperty(\"name\").GetString(); doc.ProjectInformation.Number = \"Unexpected result-check edit\"; return new { success = true };", NamePlan(changedName), "Unexpected unrelated modification");
            var extraResult = await Run(client, await Args(client, extra, changedName), "failed");
            fixture.Check("verification failure retains committed original outcome", extraResult.GetProperty("outcome").GetString() == "Applied");
            fixture.Check("native change record includes intended and unrelated elements", new[] { viewId, projectId }.All(id =>
                extraResult.GetProperty("execution").GetProperty("changes").GetProperty("modified").EnumerateArray().Any(v => v.GetInt64() == id)));
            fixture.Check("unexpected native ID is explicit and semantic intended result still passed", extraResult.GetProperty("verification").GetProperty("unexpectedChanges").GetProperty("modified")
                .EnumerateArray().Any(v => v.GetInt64() == projectId)
                && extraResult.GetProperty("verification").GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "native view name")
                    .GetProperty("status").GetString() == "passed");
            fixture.Write("unexpected-change-verification.json", extraResult.GetProperty("verification"));
            var afterExtra = await fixture.Query(client, "return new { name = doc.ActiveView.Name, number = doc.ProjectInformation.Number };");
            fixture.Check("failed verification does not automatically undo committed changes", afterExtra.GetProperty("name").GetString() == changedName
                && afterExtra.GetProperty("number").GetString() == "Unexpected result-check edit");
            await fixture.Undo(client, originalName, originalNumber);

            var missing = await Save(client, "return true;", Plan([Check("required native object", "var e = doc.GetElement(new ElementId(9223372036854770000L)); return new { exists = e != null, value = e == null ? null : e.Name };", "exists")]), "Missing object check", "query");
            await Run(client, await Args(client, missing, originalName), "failed");
            await ObserverErrors(client);
            await Additions(client);
            await ObservationBudgets(client);
            await DeletedObject(client);
            await Standalone(client);
            await StaleAndMissing(client);
            await fixture.Preservation(client); fixture.Finish(true);
        }
        catch (Exception error) { fixture.Finish(false, error); throw; }
    }
    private async Task ObserverErrors(McpClient client)
    {
        foreach (var item in new[]
        {
            ("observer compile error", "return missing_observer_symbol;"),
            ("observer runtime error", "throw new InvalidOperationException(\"expected observer error\");"),
            ("observer oversized result", "return new { exists = true, value = new string('x', 10000) };"),
            ("observer cannot persist Parameter.Set", "doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_NAME).Set(\"Observer must not persist\"); return new { exists = true, value = \"Observer must not persist\" };"),
            ("observer cannot commit its own transaction and fake a pass", "using(var t = new Transaction(doc, \"Forbidden observation edit\")) { t.Start(); doc.ActiveView.Name = \"Observer committed change\"; t.Commit(); } return new { exists = true, value = inputs.GetProperty(\"name\").GetString() };")
        })
        {
            var tool = await Save(client, "return new { success = true };", NamePlan(originalName, item.Item2), item.Item1, "query");
            await Run(client, await Args(client, tool, originalName), "error");
            fixture.Check(item.Item1 + " preserves native state", (await fixture.Query(client, "return doc.ActiveView.Name;")).GetString() == originalName);
        }
    }
    private async Task DeletedObject(McpClient client)
    {
        var created = await Modify(client, "var e = DataStorage.Create(doc); return new { id = e.Id.Value, uniqueId = e.UniqueId };");
        var element = created.GetProperty("execution").GetProperty("value"); long id = element.GetProperty("id").GetInt64();
        var plan = Plan([Check("required object after execution", "var e = doc.GetElement(new ElementId(" + id + "L)); return new { exists = e != null, value = e == null ? null : e.UniqueId };", "exists")],
            "return new { modified = System.Array.Empty<long>(), deleted = new long[] { " + id + "L } };");
        var tool = await Save(client, "doc.Delete(new ElementId(" + id + "L)); return true;", plan, "Deleted required object");
        var result = await Run(client, await Args(client, tool, originalName), "failed");
        fixture.Check("deleted native object cannot falsely satisfy existence check", result.GetProperty("execution").GetProperty("changes").GetProperty("deleted").EnumerateArray().Any(v => v.GetInt64() == id));
        await fixture.Undo(client, originalName, originalNumber);
        fixture.Check("Undo independently restores deleted native object", (await fixture.Query(client, "return doc.GetElement(new ElementId(" + id + "L)) != null;")).GetBoolean());
        await fixture.Undo(client, originalName, originalNumber);
        fixture.Check("seed cleanup Undo removes only the owned created object", !(await fixture.Query(client, "return doc.GetElement(new ElementId(" + id + "L)) != null;")).GetBoolean());
    }
    private async Task Additions(McpClient client)
    {
        var categories = await fixture.Query(client, "return new { genericModel = (long)BuiltInCategory.OST_GenericModel, walls = (long)BuiltInCategory.OST_Walls };");
        long generic = categories.GetProperty("genericModel").GetInt64(), walls = categories.GetProperty("walls").GetInt64();
        foreach (var scenario in new[] { (Count: 1, Maximum: 1, Category: generic, Status: "passed"),
            (Count: 1, Maximum: 1, Category: walls, Status: "failed"), (Count: 2, Maximum: 1, Category: generic, Status: "failed") })
        {
            string source = "var loop = new CurveLoop(); var p = new[] { new XYZ(0,0,0), new XYZ(1,0,0), new XYZ(1,1,0), new XYZ(0,1,0) }; for(int i=0;i<4;i++) loop.Append(Line.CreateBound(p[i],p[(i+1)%4])); "
                + "var solid = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisZ, 1); var ids = new List<long>(); for(int i=0;i<" + scenario.Count + ";i++) { var e = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel)); var placed = SolidUtils.CreateTransformed(solid, Transform.CreateTranslation(new XYZ(i*3,0,0))); e.SetShape(new GeometryObject[] { placed }); ids.Add(e.Id.Value); } return new { ids };";
            var plan = new
            {
                checks = new[] { Check("native additions exist", "var ids = result.GetProperty(\"ids\").EnumerateArray().Select(v=>v.GetInt64()).ToArray(); return new { exists = ids.All(id=>doc.GetElement(new ElementId(id)) != null), value = ids.Length };", "equals", scenario.Count) },
                allowedChangesSource = "return new { modified = System.Array.Empty<long>(), deleted = System.Array.Empty<long>() };",
                maxAdded = scenario.Maximum, addedCategoryIds = new[] { scenario.Category }
            };
            var saved = await Save(client, source, plan, "Native categorized additions");
            var result = await Run(client, await Args(client, saved, originalName), scenario.Status);
            var actual = result.GetProperty("execution").GetProperty("changes").GetProperty("added").EnumerateArray().Select(v=>v.GetInt64()).Order().ToArray();
            fixture.Check("original native event IDs account for every declared addition", actual.Length == scenario.Count
                && actual.SequenceEqual(result.GetProperty("execution").GetProperty("value").GetProperty("ids").EnumerateArray().Select(v=>v.GetInt64()).Order()));
            var verification = result.GetProperty("verification");
            fixture.Check("native intended additions semantic check passes independently of policy", verification.GetProperty("checks")[0].GetProperty("status").GetString() == "passed");
            var unexpected = verification.GetProperty("unexpectedChanges").GetProperty("added").EnumerateArray().Select(v=>v.GetInt64()).ToArray();
            fixture.Check("declared category/count acceptance or violation is explicit", scenario.Status == "passed" ? unexpected.Length == 0 : actual.All(unexpected.Contains));
            if (scenario.Count > scenario.Maximum)
                fixture.Check("exceeded added count reports its declared maximum violation", verification.GetProperty("violations").EnumerateArray().Any(v=>v.GetString()!.Contains("maximum",StringComparison.OrdinalIgnoreCase)));
            string idsLiteral = "new long[] { " + string.Join(',', actual.Select(id=>id+"L")) + " }";
            fixture.Check("independent native readback confirms intended category", (await fixture.Query(client, "return " + idsLiteral + ".All(id=>doc.GetElement(new ElementId(id))?.Category?.Id.Value == " + generic + "L);")).GetBoolean());
            await fixture.Undo(client, originalName, originalNumber);
            var deadline = DateTime.UtcNow.AddSeconds(20); bool removed;
            do { await Task.Delay(200); removed = (await fixture.Query(client, "return " + idsLiteral + ".All(id=>doc.GetElement(new ElementId(id)) == null);")).GetBoolean(); }
            while (!removed && DateTime.UtcNow < deadline);
            fixture.Check("one native Undo removes all owned categorized additions", removed);
        }
    }
    private async Task ObservationBudgets(McpClient client)
    {
        foreach (bool slow in new[] { false, true })
        {
            string id = Guid.NewGuid().ToString(), source = "return doc.ActiveView.Name;", hash = RevitThyme.Core.ResultChecks.Hash(source);
            var plan = NamePlan(originalName, (slow ? "System.Threading.Thread.Sleep(250); " : "") + "return new { exists = true, value = doc.ActiveView.Name };");
            var context = await fixture.Context(client); var prepare = VerifyArgs(id, plan, originalName, hash);
            prepare["captureId"] = context.GetProperty("captureId").GetString(); prepare["budgetMilliseconds"] = 100;
            var prepared = await fixture.Call(client, "prepare_result_checks", prepare);
            fixture.Check("100ms immediate native policy observation excludes compile and ID-scan overhead", prepared.GetProperty("receipt").GetProperty("succeeded").GetBoolean());
            await fixture.Call(client, "run_query", new() { ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = id, ["source"] = source, ["trusted"] = true });
            var verify = VerifyArgs(id, plan, originalName, hash); verify["budgetMilliseconds"] = 100;
            var result = await fixture.Call(client, "verify_result", verify, slow);
            fixture.Check(slow ? "bounded slow observer fails the cooperative execution budget" : "100ms immediate observer succeeds independently of compilation time",
                result.GetProperty("verification").GetProperty("status").GetString() == (slow ? "error" : "passed"));
            if (slow) fixture.Check("slow observer failure identifies budget exhaustion", result.GetProperty("verification").GetProperty("message").GetString()!.Contains("budget_exceeded", StringComparison.Ordinal));
        }
    }
    private async Task Standalone(McpClient client)
    {
        string name = "Standalone " + Guid.NewGuid().ToString("N")[..8];
        string source = "doc.ActiveView.Name = " + JsonSerializer.Serialize(name) + "; return true;";
        string hash = RevitThyme.Core.ResultChecks.Hash(source), id = Guid.NewGuid().ToString();
        var plan = NamePlan(name); var context = await fixture.Context(client);
        var prepareArgs = VerifyArgs(id, plan, name, hash); prepareArgs["captureId"] = context.GetProperty("captureId").GetString();
        var prep = await fixture.Call(client, "prepare_result_checks", prepareArgs);
        var again = await fixture.Call(client, "prepare_result_checks", prepareArgs);
        fixture.Check("standalone preparation IDs are deterministic and distinct from action", prep.GetProperty("preparationId").GetString() == again.GetProperty("preparationId").GetString()
            && prep.GetProperty("verificationId").GetString() == again.GetProperty("verificationId").GetString()
            && prep.GetProperty("operationId").GetString() == id && prep.GetProperty("preparationId").GetString() != id);
        var action = await fixture.Call(client, "run_modify", new()
        { ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = id, ["source"] = source, ["trusted"] = true });
        var verified = await fixture.Call(client, "verify_result", VerifyArgs(id, plan, name, hash));
        fixture.Check("standalone comparison verifies retained original without resubmitting action", verified.GetProperty("verification").GetProperty("status").GetString() == "passed"
            && verified.GetProperty("id").GetString() == action.GetProperty("id").GetString()
            && verified.GetProperty("verification").GetProperty("preparationId").GetString() == prep.GetProperty("preparationId").GetString()
            && verified.GetProperty("verification").GetProperty("verificationId").GetString() == prep.GetProperty("verificationId").GetString());
        await using (var restarted = await fixture.Client())
        {
            var recovery = await fixture.Call(restarted, "verify_result", VerifyArgs(id, plan, name, hash));
            fixture.Check("native prepared policy and checker receipts survive client restart", recovery.GetProperty("verification").GetProperty("status").GetString() == "passed"
                && recovery.GetProperty("verification").GetProperty("verificationId").GetString() == prep.GetProperty("verificationId").GetString());
        }
        foreach (var invalid in new[]
        {
            VerifyArgs(id, NamePlan("changed expected plan"), name, hash),
            VerifyArgs(id, plan, "different inputs", hash),
            VerifyArgs(id, plan, name, new string('0', 64))
        })
        {
            var rejected = await fixture.Call(client, "verify_result", invalid, true);
            fixture.Check("changed plan, inputs or source cannot reuse earlier verification attribution", rejected.GetProperty("verification").GetProperty("status").GetString() != "passed");
        }
        await fixture.Undo(client, originalName, originalNumber);
    }
    private static Dictionary<string, object?> VerifyArgs(string id, object plan, string name, string hash) => new()
    { ["requestId"] = id, ["plan"] = plan, ["inputs"] = new { name }, ["sourceSha256"] = hash, ["trusted"] = true };
    private async Task StaleAndMissing(McpClient client)
    {
        var plan = NamePlan(originalName); string querySource = "return doc.ActiveView.Name;", hash = RevitThyme.Core.ResultChecks.Hash(querySource);
        var missing = await fixture.Call(client, "verify_result", VerifyArgs(Guid.NewGuid().ToString(), plan, originalName, hash), true);
        fixture.Check("missing retained original outcome is unconfirmed", missing.GetProperty("verification").GetProperty("status").GetString() == "unconfirmed");
        string noPrepId = Guid.NewGuid().ToString(); var context = await fixture.Context(client);
        await fixture.Call(client, "run_query", new() { ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = noPrepId, ["source"] = querySource, ["trusted"] = true });
        var noPrep = await fixture.Call(client, "verify_result", VerifyArgs(noPrepId, plan, originalName, hash), true);
        fixture.Check("native action without a prepared policy cannot pass verification", noPrep.GetProperty("verification").GetProperty("status").GetString() == "unconfirmed");
        string name = "Stale Check " + Guid.NewGuid().ToString("N")[..8], id = Guid.NewGuid().ToString();
        string source = "doc.ActiveView.Name = " + JsonSerializer.Serialize(name) + "; return true;"; hash = RevitThyme.Core.ResultChecks.Hash(source); plan = NamePlan(name);
        context = await fixture.Context(client); var prepare = VerifyArgs(id, plan, name, hash); prepare["captureId"] = context.GetProperty("captureId").GetString();
        await fixture.Call(client, "prepare_result_checks", prepare);
        await fixture.Call(client, "run_modify", new() { ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = id, ["source"] = source, ["trusted"] = true });
        await Modify(client, "doc.ProjectInformation.Number = \"Intervening revision\"; return true;");
        var stale = await fixture.Call(client, "verify_result", VerifyArgs(id, plan, name, hash), true);
        fixture.Check("intervening native revision cannot be attributed to the original action", stale.GetProperty("outcome").GetString() == "Applied"
            && stale.GetProperty("verification").GetProperty("status").GetString() == "unconfirmed");
        await fixture.Undo(client, name, originalNumber);
        await fixture.Undo(client, originalName, originalNumber);
    }
}
