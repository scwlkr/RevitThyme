using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static partial class LifecycleCoordination
{
    private static void Boundaries(Action<string, bool> check)
    {
        (string Name, Action<Doc> Apply)[] activeFlags =
        [
            ("modifiable", d => d.IsModifiable = true), ("read-only", d => d.IsReadOnly = true),
            ("family", d => d.IsFamilyDocument = true), ("workshared", d => d.IsWorkshared = true),
        ];
        foreach (var flag in activeFlags)
        {
            var runtime = new Runtime(); flag.Apply(runtime.A);
            Expect("active " + flag.Name + " is independently refused before file access", runtime, OperationKind.OpenDocument,
                runtime.Request(runtime.C), "lifecycle_blocked", () => runtime.Events.SequenceEqual(["scope:" + runtime.C.PathName]));
        }
        (string Name, Action<Doc> Apply)[] targetFlags =
        [
            ("linked", d => d.IsLinked = true), ("family", d => d.IsFamilyDocument = true),
            ("workshared", d => d.IsWorkshared = true), ("read-only", d => d.IsReadOnly = true), ("modifiable", d => d.IsModifiable = true),
        ];
        foreach (var flag in targetFlags)
        {
            var runtime = new Runtime(); flag.Apply(runtime.B);
            Expect("target " + flag.Name + " is independently refused before activation", runtime, OperationKind.ActivateDocument,
                runtime.Request(runtime.B), "lifecycle_blocked", () => runtime.Activates == 0);
        }
        (string Name, Action<Doc> Apply)[] finalChanges =
        [
            ("invalid object", d => d.IsValidObject = false), ("workshared", d => d.IsWorkshared = true),
            ("family", d => d.IsFamilyDocument = true), ("linked", d => d.IsLinked = true),
            ("different path", d => d.PathName += ".different"),
        ];
        foreach (var change in finalChanges)
        {
            var runtime = new Runtime(); var options = runtime.Request(runtime.C);
            runtime.AfterOpen = () => change.Apply(runtime.C);
            Expect("post-open " + change.Name + " remains unconfirmed without retry", runtime, OperationKind.OpenDocument,
                options, "lifecycle_unconfirmed", () => runtime.Opens == 1 && runtime.Activates == 0);
        }
        var missing = new Runtime { ReturnNullOpen = true };
        Expect("null native open result remains unconfirmed", missing, OperationKind.OpenDocument, missing.Request(missing.C), "lifecycle_unconfirmed", () => missing.Opens == 1);
        var retained = new Runtime(); retained.B.CloseAction = () => { };
        Expect("successful close with retained document membership remains unconfirmed", retained, OperationKind.CloseDocument,
            retained.Request(retained.B), "lifecycle_unconfirmed", () => retained.B.Closes == 1);
        var changedClose = new Runtime(); changedClose.AfterClose = () => changedClose.Hashes[changedClose.B.PathName] = ChangedHash;
        Expect("close with independently changed file bytes remains unconfirmed", changedClose, OperationKind.CloseDocument,
            changedClose.Request(changedClose.B), "lifecycle_unconfirmed", () => changedClose.B.Closes == 1);
        (string Name, Action<Doc> Apply)[] closeChanges =
        [
            ("invalidated", d => d.IsValidObject = false), ("changed ID", d => d.Id = "new-id"),
            ("changed path", d => d.PathName += ".different"),
        ];
        foreach (var change in closeChanges)
        {
            var runtime = new Runtime();
            var options = runtime.Request(runtime.A) with { ActivateBeforeClose = runtime.Target(runtime.B) };
            runtime.AfterActivate = () => change.Apply(runtime.A);
            Expect("activation callback with " + change.Name + " close target never closes it", runtime, OperationKind.CloseDocument,
                options, "lifecycle_unconfirmed", () => runtime.A.Closes == 0 && runtime.Active == runtime.B);
        }
        var alias = new Runtime(); alias.Active = new Doc("a") { Identity = alias.A.Identity };
        check("activation recognizes native identity across distinct wrappers", !alias.Change(OperationKind.ActivateDocument, alias.Request(alias.A)).Performed && alias.Activates == 0);
        alias.A.IsModified = true;
        check("save recognizes native identity across distinct wrappers", alias.Change(OperationKind.SaveDocument, alias.Request(alias.A)).Performed && alias.A.Saves == 1);
        var home = new Runtime { Active = null };
        check("background open from Home retains no active document", home.Change(OperationKind.OpenDocument, home.Request(home.C)) is { IsActive: false, Performed: true } && home.Active is null);
        var noActiveSave = new Runtime { Active = null };
        Expect("save without an active document is unconfirmed without saving", noActiveSave, OperationKind.SaveDocument,
            noActiveSave.Request(noActiveSave.A), "lifecycle_unconfirmed", () => noActiveSave.A.Saves == 0);
        var inactiveSave = new Runtime();
        Expect("inactive save is unconfirmed without saving", inactiveSave, OperationKind.SaveDocument,
            inactiveSave.Request(inactiveSave.B), "lifecycle_unconfirmed", () => inactiveSave.B.Saves == 0);
        var sharedHeader = new Runtime(); sharedHeader.Header.IsWorkshared = true;
        Expect("workshared file metadata is refused and disposed", sharedHeader, OperationKind.OpenDocument,
            sharedHeader.Request(sharedHeader.C), "unsupported_document", () => sharedHeader.Header.Disposed && sharedHeader.Opens == 0);

        void Expect(string name, Runtime runtime, OperationKind operation, DocumentLifecycleRequest options, string code, Func<bool> outcome)
        {
            string? actual = null;
            try { runtime.Change(operation, options); } catch (OperationException error) { actual = error.Code; }
            check(name, actual == code && outcome());
        }
    }
}
