using RevitThyme.Quality;

int failures = 0;
int total = 0;
void Check(string name, bool passed)
{
    total++;
    Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
    if (!passed) failures++;
}
var straight = SourceAnalysis.Parse("class C { int F(int n) => n + 1; }", "src/C.cs");
Check("straight-line expression has complexity one", straight is [{ Complexity: 1, Name: "C.F" }]);
var branching = SourceAnalysis.Parse("class C { int F(int n) { if(n > 0 && n < 10) return n; return 0; } }", "src/C.cs");
Check("conditional and short circuit count separately", branching is [{ Complexity: 3 }]);
var nested = SourceAnalysis.Parse("class C { int F(int n) { int G(int x) { if(x > 0) return x; return 0; } return G(n); } }", "src/C.cs");
Check("local function owns its decisions", nested.Count == 2 && nested[0].Complexity == 1 && nested[1].Complexity == 2);
var lambda = SourceAnalysis.Parse("class C { System.Func<int,int> F() => n => n > 0 ? n : 0; }", "src/C.cs");
Check("lambda owns its decisions", lambda.Count == 2 && lambda[0].Complexity == 1 && lambda[1].Complexity == 2);
var modern = SourceAnalysis.Parse("record C(int X) { int P => X switch { > 0 => 1, _ => 0 }; int V { get { return X; } } }", "src/C.cs");
Check("records, expression properties, accessors and switch default supported", modern.Count == 2 && modern[0].Complexity == 2);
bool invalid = false;
try { SourceAnalysis.Parse("class {", "src/C.cs"); } catch (InvalidDataException) { invalid = true; }
Check("malformed C# cannot produce a green report", invalid);
var renamed = SourceAnalysis.Parse("class D { int G(int value) => value + 42; }", "src/D.cs");
Check("duplicate normalization ignores incidental names and literal values", Duplication.Similarity(straight[0], renamed[0]) == 1);
var calls = SourceAnalysis.Parse("class C { int F(int n) => Read(n); int G(int n) => Write(n); }", "src/C.cs");
Check("duplicate normalization preserves called method names", Duplication.Similarity(calls[0], calls[1]) < 1);
var members = SourceAnalysis.Parse("class C { int F() => owner.Read(); int G() => owner.Write(); }", "src/C.cs");
Check("duplicate normalization preserves member callees", Duplication.Similarity(members[0], members[1]) < 1);
var conditionalCalls = SourceAnalysis.Parse("class C { int? F() => owner?.Read(); int? G() => owner?.Write(); }", "src/C.cs");
Check("duplicate normalization preserves conditional member callees", Duplication.Similarity(conditionalCalls[0], conditionalCalls[1]) < 1);
var generics = SourceAnalysis.Parse("class C { int F(int n) => Read<int>(n); int G(int n) => Write<int>(n); }", "src/C.cs");
Check("duplicate normalization preserves generic callees", Duplication.Similarity(generics[0], generics[1]) < 1);
var constructors = SourceAnalysis.Parse("class C { object F() => new First(); object G() => new Second(); }", "src/C.cs");
Check("duplicate normalization preserves constructed types", Duplication.Similarity(constructors[0], constructors[1]) < 1);
var initializer = SourceAnalysis.Parse("class C { C(int n) : this(n > 0 ? n : 0, true) {} }", "src/C.cs");
Check("constructor initializer decisions belong to their constructor", initializer is [{ Complexity: 2 }]);
var sites = MutationSites.Find("class C { bool F(int n) => n > 0 && true; string G() => \"n > 0 && true\"; }", "src/C.cs");
Check("mutator finds syntax operators and booleans, never string content", sites.Count == 4 && sites.All(s => s.UnitName == "C.F"));
Check("mutation replaces exactly one source span", sites.Count(s => s.Apply().Contains("n >= 0", StringComparison.Ordinal)) == 1);
var patterns = MutationSites.Find("class C { bool F(int n) => n is >= 0 and <= 10; }", "src/C.cs");
Check("C# relational and logical patterns are mutation sites", patterns.Count == 4 && patterns.Any(s => s.Original == "and" && s.Replacement == "or"));
var negation = MutationSites.Find("class C { bool F(bool value) => !value; }", "src/C.cs");
Check("logical negation can be removed without changing its operand", negation is [{ Original: "!", Replacement: "" }] && negation[0].Apply().Contains("=> value;", StringComparison.Ordinal));
var notPattern = MutationSites.Find("class C { bool F(string? value) => value is not null; }", "src/C.cs");
Check("not patterns are inverted at their syntax token", notPattern is [{ Original: "not", Replacement: "" }] && notPattern[0].Apply().Contains("is  null", StringComparison.Ordinal));
Check("CRAP formula uses measured fractional coverage", Math.Abs(Complexity.Crap(4, .5) - 6) < 1e-9);
Check("full coverage does not hide complexity", Complexity.Crap(10, 1) == 10);
Check("uncovered code retains risk", Complexity.Crap(4, 0) == 20);
var success = new CommandResult(0, false, 0);
var failure = new CommandResult(1, false, 0, CompilerFailure: true);
var timeout = new CommandResult(null, true, 0);
Check("a compiler failure is invalid rather than killed", MutationRunner.Classify(failure, [success]) == MutationState.Invalid);
Check("a passing mutant survives", MutationRunner.Classify(success, [success]) == MutationState.Survived);
Check("only a failing executed test kills a compiling mutant", MutationRunner.Classify(success, [failure]) == MutationState.Killed);
Check("a test timeout remains unresolved", MutationRunner.Classify(success, [timeout]) == MutationState.Unconfirmed);
Check("a build timeout remains unresolved", MutationRunner.Classify(timeout, []) == MutationState.Unconfirmed);
Check("a restore or infrastructure failure is not a compile-invalid mutant", MutationRunner.Classify(new(1, false, 0), []) == MutationState.Unconfirmed);
Check("a mutant with no tests cannot pass", MutationRunner.Classify(success, []) == MutationState.Unconfirmed);
string directory = Path.Combine(Path.GetTempPath(), "RevitThyme-Quality-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
string firstReport = Path.Combine(directory, "first.xml"), secondReport = Path.Combine(directory, "second.xml");
try
{
    string Report(int one, int two) => $"<coverage><packages><package><classes><class filename=\"src/C.cs\"><lines><line number=\"1\" hits=\"{one}\"/><line number=\"2\" hits=\"{two}\"/></lines></class></classes></package></packages></coverage>";
    File.WriteAllText(firstReport, Report(1, 0)); File.WriteAllText(secondReport, Report(0, 1));
    var unit = SourceAnalysis.Parse("class C {\nint F() => 1;\n}", "src/C.cs")[0];
    var half = Coverage.Load(directory, firstReport).For(directory, unit);
    Check("coverage uses executable line counts rather than file totals", half.CoveredLines == 0 && half.ExecutableLines == 1);
    var union = Coverage.Load(directory, firstReport, secondReport).For(directory, unit);
    Check("coverage unions real hits across suites", union.CoveredLines == 1 && union.ExecutableLines == 1);
    Check("absent native code remains unmeasured", Coverage.Load(directory, firstReport).For(directory,
        SourceAnalysis.Parse("class C { int F() => 1; }", "src/Missing.cs")[0]).ExecutableLines == 0);
    File.WriteAllText(secondReport, "<coverage><packages><package><classes><class filename=\"empty.cs\"><lines/></class></classes></package></packages></coverage>");
    bool empty = false;
    try { Coverage.Load(directory, secondReport); } catch (InvalidDataException) { empty = true; }
    Check("an empty class cannot masquerade as executed coverage", empty);
    bool escaped = false;
    try { SourceSnapshot.Resolve(directory, "../escape.cs"); } catch (InvalidDataException) { escaped = true; }
    Check("worker copy rejects paths outside its root", escaped);
}
finally { File.Delete(firstReport); File.Delete(secondReport); Directory.Delete(directory); }
await CliIntegration.Run(Check);
await MutationIntegration.Run(Check);
Console.WriteLine($"Quality tool checks: {total - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;
