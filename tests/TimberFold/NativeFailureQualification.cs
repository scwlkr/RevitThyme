using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitThyme.TimberFold.Fixtures;

// Registers only a test observer. All generation and cancellation use the already
// installed public host; no replacement product code or dispatcher is loaded.
public static class NativeFailureQualification
{
    private static readonly Dictionary<Guid, Observer> Registrations = [];

    public static bool HasObserver(string observerToken) => Guid.TryParseExact(observerToken, "D", out var token) && Registrations.ContainsKey(token);

    public static object Begin(UIApplication uiapp, Guid generationId, string mode, string expectedTargetJson,
        string evidenceDirectory, int timeoutSeconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (generationId == Guid.Empty || mode is not ("export" or "publication" or "cancel") || timeoutSeconds is < 10 or > 180 || expectedTargetJson.Length > 16384)
            throw new ArgumentException("Use a new generation GUID, export/publication/cancel mode and 10..180 second observer budget.");
        var pin = JsonSerializer.Deserialize<NativeFailureEvidence.Pin>(expectedTargetJson, NativeFailureEvidence.Json)
            ?? throw new InvalidDataException("Explicit target pin is required.");
        var (source, host) = NativeFailureEvidence.Guard(uiapp, pin, true);
        string generation = Path.Combine(NativeFailureEvidence.RunsRoot, generationId.ToString("D"));
        if (Directory.Exists(generation) || File.Exists(generation)) throw new InvalidDataException("Arm only a fresh planned generation, never a retained run.");
        if (!Path.IsPathFullyQualified(evidenceDirectory)) throw new InvalidDataException("Use fresh absolute evidence outside generation storage.");
        evidenceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceDirectory));
        if (Directory.Exists(evidenceDirectory) || File.Exists(evidenceDirectory) || NativeFailureEvidence.Inside(NativeFailureEvidence.RunsRoot, evidenceDirectory))
            throw new InvalidDataException("Preserve prior evidence and all generation storage.");
        string parent = Path.GetDirectoryName(evidenceDirectory)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Evidence parent must already exist.");
        NativeFailureEvidence.Plain(parent);
        if (Registrations.Count >= 8 || Registrations.Values.Any(o => o.GenerationId == generationId))
            throw new InvalidOperationException("Stop prior registrations; observer capacity is eight.");
        var catalog = NativeFailureEvidence.Catalog(source, ct);
        string savedHash = NativeFailureEvidence.Hash(source.PathName, ct);
        Guid token = Guid.NewGuid();
        Directory.CreateDirectory(evidenceDirectory);
        var observer = new Observer(uiapp, source, host, pin, token, generationId, mode, generation, evidenceDirectory,
            catalog, savedHash, DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds));
        NativeFailureEvidence.Write(evidenceDirectory, "before.json", new { schema = 1, observerToken = token, generationId, mode, pin,
            sourcePath = source.PathName, sourceWasModified = source.IsModified, viewId = source.ActiveView.Id.Value,
            savedSourceSha256 = savedHash, beforeCatalog = catalog, observer.Deadline, assemblyMvid = typeof(NativeFailureQualification).Module.ModuleVersionId });
        Registrations.Add(token, observer);
        try { observer.Attach(); }
        catch { Registrations.Remove(token); observer.Detach("RegistrationFailed", null); throw; }
        return observer.Description();
    }

    public static object Stop(UIApplication uiapp, string observerToken, CancellationToken ct)
    {
        var observer = Find(observerToken);
        observer.Detach("Stopped", null); Registrations.Remove(observer.Token);
        return observer.Description();
    }

    public static object CheckRollback(UIApplication uiapp, string observerToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var observer = Find(observerToken);
        if (observer.Mode == "publication") throw new InvalidOperationException("Publication fault commits native documentation; use the independent committed-kit audit and explicit reconciliation.");
        var (source, _) = NativeFailureEvidence.Guard(uiapp, observer.Pin, false);
        if (!source.Equals(observer.Source)) throw new InvalidDataException("Original native document wrapper changed.");
        var current = NativeFailureEvidence.Catalog(source, ct);
        var receipt = NativeFailureEvidence.Read(Path.Combine(observer.GenerationDirectory, "native-receipt.json"));
        var run = NativeFailureEvidence.Read(Path.Combine(observer.GenerationDirectory, "run.json"));
        var diagnostics = NativeFailureEvidence.Property(receipt, "diagnostics").EnumerateArray().ToArray();
        bool passed = observer.Outcome == "Triggered" && observer.Added.Length > 0 && current.SequenceEqual(observer.Before) &&
            source.IsModified == observer.WasModified && source.ActiveView.Id.Value == observer.ViewId && uiapp.Application.Documents.Size == 1 &&
            NativeFailureEvidence.Hash(source.PathName, ct) == observer.SavedHash && observer.Added.All(id => source.GetElement(new ElementId(id)) is null) &&
            NativeFailureEvidence.Property(receipt, "runId").GetGuid() == observer.GenerationId &&
            NativeFailureEvidence.Property(receipt, "state").GetString() == "RolledBack" && NativeFailureEvidence.Property(receipt, "sourcePreserved").GetBoolean() &&
            !NativeFailureEvidence.Property(receipt, "nativeReadbackPassed").GetBoolean() &&
            NativeFailureEvidence.Property(run, "runId").GetGuid() == observer.GenerationId && !NativeFailureEvidence.Property(run, "nativeCommitted").GetBoolean() &&
            NativeFailureEvidence.Property(run, "state").GetString() == (observer.Mode == "cancel" ? "Cancelled" : "Failed") &&
            (observer.Mode != "cancel" || diagnostics.Any(d => NativeFailureEvidence.Property(d, "code").GetString() == "cancelled")) &&
            !File.Exists(Path.Combine(observer.GenerationDirectory, "kit.json")) && !Directory.Exists(Path.Combine(observer.GenerationDirectory, "kit.json")) &&
            !File.Exists(Path.Combine(observer.GenerationDirectory, "native-prepared.json"));
        string name = "rollback-" + Guid.NewGuid().ToString("N") + ".json";
        var report = new { schema = 1, passed, observerToken = observer.Token, observer.GenerationId, observer.Mode,
            originalCatalogUnchanged = current.SequenceEqual(observer.Before), originalModifiedState = observer.WasModified,
            currentModifiedState = source.IsModified, allObservedAddedIdsAbsent = observer.Added.All(id => source.GetElement(new ElementId(id)) is null),
            savedSourceUnchanged = NativeFailureEvidence.Hash(source.PathName, ct) == observer.SavedHash, afterCatalog = current, receipt, run,
            productBoundary = "Actual installed public generation/coordinator; one-shot test observer triggers a bounded owned-output or control fault.", physicalChecksOutstanding = true };
        NativeFailureEvidence.Write(observer.EvidenceDirectory, name, report);
        if (!passed) throw new InvalidDataException("Actual native failure/rollback qualification failed; retain observer and generation evidence.");
        return new { passed, reportPath = Path.Combine(observer.EvidenceDirectory, name), observerToken = observer.Token };
    }

    private static Observer Find(string value) => Guid.TryParseExact(value, "D", out var token) && Registrations.TryGetValue(token, out var observer)
        ? observer : throw new InvalidOperationException("Observer token is absent from this assembly instance; locate the registered MVID, never byte-load another copy.");

    private sealed class Observer(UIApplication app, Document source, object host, NativeFailureEvidence.Pin pin, Guid token,
        Guid generationId, string mode, string generationDirectory, string evidenceDirectory, NativeFailureEvidence.Stamp[] before,
        string savedHash, DateTimeOffset deadline)
    {
        internal Guid Token => token;
        internal Guid GenerationId => generationId;
        internal string Mode => mode;
        internal string GenerationDirectory => generationDirectory;
        internal string EvidenceDirectory => evidenceDirectory;
        internal NativeFailureEvidence.Pin Pin => pin;
        internal Document Source => source;
        internal NativeFailureEvidence.Stamp[] Before => before;
        internal string SavedHash => savedHash;
        internal DateTimeOffset Deadline => deadline;
        internal bool WasModified { get; } = source.IsModified;
        internal long ViewId { get; } = source.ActiveView.Id.Value;
        internal long[] Added { get; private set; } = [];
        internal string Outcome { get; private set; } = "Armed";
        private string? observationError;
        private bool subscribed;

        internal object Description() => new { observerToken = token, generationId, mode, Outcome, deadline, observationError,
            generationDirectory, evidenceDirectory, assemblyMvid = typeof(NativeFailureQualification).Module.ModuleVersionId,
            stopRequiredInFinally = true, expiry = "API-thread Idling or next DocumentChanged callback; no background Revit API calls." };
        internal void Attach()
        {
            subscribed = true; app.Application.DocumentChanged += Changed; app.Idling += Idle;
        }
        internal void Detach(string outcome, object? detail)
        {
            if (!subscribed) return;
            app.Application.DocumentChanged -= Changed; app.Idling -= Idle; subscribed = false;
            Outcome = outcome;
            try
            {
                NativeFailureEvidence.Write(evidenceDirectory, "observer.json", new { schema = 1, observerToken = token,
                    generationId, mode, outcome, deadline, addedIds = Added, detail, triggeredAtUtc = DateTimeOffset.UtcNow });
            }
            catch (Exception error) { observationError = error.ToString(); Outcome = "Failed"; }
        }
        private void Idle(object? sender, IdlingEventArgs args)
        {
            if (DateTimeOffset.UtcNow >= deadline) Detach("Expired", null);
        }
        private void Changed(object? sender, DocumentChangedEventArgs args)
        {
            if (DateTimeOffset.UtcNow >= deadline) { Detach("Expired", null); return; }
            if (!source.Equals(args.GetDocument()) || !args.GetTransactionNames().Contains("TimberFold review documentation", StringComparer.Ordinal)) return;
            string prefix = "TF-" + generationId.ToString("N")[..12] + " ";
            var added = args.GetAddedElementIds().Select(id => id.Value).ToArray();
            if (!added.Any(id => source.GetElement(new ElementId(id)) is ViewDrafting view && view.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                NativeFailureEvidence.OwnedDraftingView(view, generationId))) return;
            try
            {
                var (current, _) = NativeFailureEvidence.Guard(app, pin, false);
                if (!source.Equals(current)) throw new InvalidDataException("Native fault event retargeted another source.");
                NativeFailureEvidence.Plain(generationDirectory);
                var request = NativeFailureEvidence.Read(Path.Combine(generationDirectory, "request.json"));
                if (NativeFailureEvidence.Property(request, "id").GetGuid() != generationId) throw new InvalidDataException("Owned generation request differs.");
                var requestPin = NativeFailureEvidence.Property(request, "target").Deserialize<NativeFailureEvidence.Pin>(NativeFailureEvidence.Json)
                    ?? throw new InvalidDataException("Owned generation request target is missing.");
                if ((requestPin with { Revision = pin.Revision }) != pin)
                    throw new InvalidDataException("Owned generation request process/start/session/document/view differs.");
                Added = added;
                // Detach before acting: rollback may itself issue DocumentChanged.
                app.Application.DocumentChanged -= Changed; app.Idling -= Idle;
                object detail;
                if (mode == "export")
                {
                    string output = Path.Combine(generationDirectory, "native-cuts"); NativeFailureEvidence.Plain(output);
                    if (Directory.GetFileSystemEntries(output).Length != 0) throw new InvalidDataException("Expected exact empty native export output.");
                    string retained = Path.Combine(generationDirectory, "fault-export-output-" + token.ToString("N"));
                    if (Directory.Exists(retained) || File.Exists(retained)) throw new IOException("Preserve existing fault output.");
                    Directory.Move(output, retained);
                    detail = new { action = "Moved exact empty owned output before real Document.Export", output, retained };
                }
                else if (mode == "publication")
                {
                    string destination = Path.Combine(generationDirectory, "kit.json");
                    if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Publication path already exists; do not modify it.");
                    Directory.CreateDirectory(destination);
                    detail = new { action = "Created one empty owned publication obstruction", destination };
                }
                else detail = new { action = "Installed public cancellation acknowledged during actual native callback", acknowledgement = NativeFailureEvidence.Cancel(host, generationId) };
                Detach("Triggered", detail);
            }
            catch (Exception error) { Detach("Failed", new { error = error.ToString() }); }
        }
    }
}
