using System.Collections.Immutable;
using System.Diagnostics;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class NativeTimberKitRecoveryChecks
{
    internal static void Run(Action<string, bool> check)
    {
        using var fixture = new NativeTimberKitInputs.Fixture();
        var target = fixture.Plan.SourceContext.Target;
        var after = target with { Revision = target.Revision + 1 };
        var progress = new NativeTimberKitProgress(fixture.Request(fixture.Plan), fixture.Plan, target,
            [new(3, "original-three", "CutImport")], [new("native-cuts/CUT-001.dwg", new string('b', 64), 12, "Cut")],
            "prepared-digest", false, true, Stopwatch.StartNew());
        Runtime Runtime() => new(fixture.Plan.SourceFingerprint, after);
        var failure = new InvalidOperationException("native failure");
        TimberNativeKitReceipt Run(Runtime runtime, NativeTimberKitProgress? state = null, Exception? error = null) =>
            NativeTimberKitRecovery.Recover(runtime, state ?? progress, error ?? failure);

        var runtime = Runtime(); var receipt = Run(runtime);
        check("native recovery confirms rollback only after preservation and fingerprint checks", receipt.State == TimberNativeKitState.RolledBack && receipt.SourcePreserved &&
            !receipt.NativeReadbackPassed && receipt.AfterTarget == after && runtime.Events.SequenceEqual(new[] { "committed", "started", "rollback", "preserve", "active", "fingerprint", "capture" }));
        check("recovery retains exact request, files, created identities, seal and prepared evidence", receipt.RunId == fixture.RunId && receipt.CreatedElements.SequenceEqual(progress.Created) &&
            receipt.Files.SequenceEqual(progress.Files) && receipt.InputSealSha256 == progress.Request.InputSealSha256 && receipt.PreparedReceiptSha256 == progress.PreparedHash && receipt.NativeMilliseconds >= 0);
        check("recovery errors retain native diagnostic severity and message", receipt.Diagnostics is [{ Code: "native_kit", Severity: TimberSeverity.Error, Message: "native failure" }]);
        check("recovery uses uncancellable original-source verification and excludes every generated element", runtime.Tokens.Count == 2 && runtime.Tokens.All(t => !t.CanBeCanceled) &&
            runtime.PreservationCommitted == false && runtime.FingerprintTarget == target && runtime.OwnedCount == 0);

        runtime = new(fixture.Plan.SourceFingerprint, after) { CommittedError = new IOException("must not read status") };
        receipt = Run(runtime, progress with { Committed = true }, new OperationCanceledException("late cancellation"));
        check("known commit wins cancellation without querying status or attempting recovery", receipt.State == TimberNativeKitState.Committed && receipt.SourcePreserved && receipt.NativeReadbackPassed &&
            receipt.AfterTarget == target && runtime.Events.Count == 0 && receipt.Diagnostics is [{ Code: "native_postcommit_context", Severity: TimberSeverity.Warning }]);
        runtime = new(fixture.Plan.SourceFingerprint, after) { GroupCommitted = true };
        receipt = Run(runtime, progress with { Readback = false });
        check("native committed status also wins an unacknowledged commit while retaining readback fact", receipt.State == TimberNativeKitState.Committed && !receipt.NativeReadbackPassed &&
            runtime.Events.SequenceEqual(new[] { "committed" }));
        receipt = Run(Runtime(), error: new OperationCanceledException("early cancellation"));
        check("precommit cancellation retains confirmed rollback and cancellation diagnostic", receipt.State == TimberNativeKitState.RolledBack && receipt.Diagnostics is [{ Code: "cancelled" }]);

        runtime = new(fixture.Plan.SourceFingerprint, after) { HasGroup = false, HasPreservation = false, GroupStarted = false };
        receipt = Run(runtime, progress with { Plan = null });
        check("failure before a native group with retained source identity is rejected", receipt.State == TimberNativeKitState.Rejected && receipt.SourcePreserved && !runtime.Events.Contains("fingerprint") && !runtime.Events.Contains("inventory"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { HasGroup = false, HasPreservation = false, GroupStarted = false, SourceActive = false };
        receipt = Run(runtime, progress with { Plan = null });
        check("failure before a native group cannot claim rejection when source identity is lost", receipt.State == TimberNativeKitState.Unconfirmed && !receipt.SourcePreserved);
        runtime = new(fixture.Plan.SourceFingerprint, after) { GroupStarted = false, GroupRolledBack = true };
        receipt = Run(runtime);
        check("already rolled-back group is verified without a second rollback", receipt.State == TimberNativeKitState.RolledBack && !runtime.Events.Contains("rollback") && runtime.Events.Contains("rolled_back"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { GroupStarted = false };
        receipt = Run(runtime);
        check("a pending group is unconfirmed even with proven source preservation", receipt.State == TimberNativeKitState.Unconfirmed && receipt.SourcePreserved && !runtime.Events.Contains("rollback"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { SourceActive = false };
        receipt = Run(runtime);
        check("lost active source still performs fingerprint verification and cannot confirm rollback", receipt.State == TimberNativeKitState.Unconfirmed && !receipt.SourcePreserved && runtime.Events.Contains("fingerprint"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { FingerprintValue = "different" };
        receipt = Run(runtime);
        check("changed source fingerprint refuses a preservation claim", receipt.State == TimberNativeKitState.Unconfirmed && !receipt.SourcePreserved);
        runtime = new(fixture.Plan.SourceFingerprint, after) { PreservationError = new IOException("preservation failed") };
        receipt = Run(runtime);
        check("failed preservation leaves recovery unconfirmed and does not capture later context", receipt.State == TimberNativeKitState.Unconfirmed && !receipt.SourcePreserved && receipt.AfterTarget == target &&
            receipt.Diagnostics[0].Message.Contains("preservation failed", StringComparison.Ordinal) && !runtime.Events.Contains("capture"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { ActiveError = new IOException("active failed") };
        receipt = Run(runtime);
        check("failed source identity lookup cannot claim preservation", receipt.State == TimberNativeKitState.Unconfirmed && !receipt.SourcePreserved && !runtime.Events.Contains("fingerprint"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { CaptureError = new IOException("capture failed") };
        receipt = Run(runtime);
        check("context capture failure retains previously completed preservation proofs and prior target", receipt.State == TimberNativeKitState.RolledBack && receipt.SourcePreserved && receipt.AfterTarget == target &&
            receipt.Diagnostics[0].Message.Contains("capture failed", StringComparison.Ordinal));
        runtime = new(fixture.Plan.SourceFingerprint, after) { RollbackSucceeded = false };
        receipt = Run(runtime);
        check("refused rollback inventories new IDs once without overwriting known identity or role", receipt.State == TimberNativeKitState.Unconfirmed && receipt.SourcePreserved &&
            receipt.CreatedElements.SequenceEqual(new TimberNativeElement[] { new(3, "original-three", "CutImport"), new(5, "unique-5", "UnconfirmedResource") }) && runtime.Events.Count(e => e == "rollback") == 1);
        runtime = new(fixture.Plan.SourceFingerprint, after) { RollbackError = new IOException("rollback failed") };
        receipt = Run(runtime);
        check("throwing rollback remains unconfirmed, inventories resources and never retries", receipt.State == TimberNativeKitState.Unconfirmed && !receipt.SourcePreserved && receipt.CreatedElements.Length == 2 &&
            runtime.Events.Count(e => e == "rollback") == 1 && !runtime.Events.Contains("preserve"));
        runtime = new(fixture.Plan.SourceFingerprint, after) { RollbackSucceeded = false, InventoryError = new IOException("inventory failed") };
        receipt = Run(runtime);
        check("failed inventory retains earlier created identities and original failure evidence", receipt.State == TimberNativeKitState.Unconfirmed && receipt.CreatedElements.SequenceEqual(progress.Created) &&
            receipt.Diagnostics[0].Message.Contains("native failure", StringComparison.Ordinal) && receipt.Diagnostics[0].Message.Contains("inventory failed", StringComparison.Ordinal));
        runtime = new(fixture.Plan.SourceFingerprint, after) { RollbackSucceeded = false, UniqueIdError = new IOException("element vanished") };
        receipt = Run(runtime);
        check("failed element lookup retains complete earlier evidence rather than a partial new inventory", receipt.CreatedElements.SequenceEqual(progress.Created) && receipt.Diagnostics[0].Message.Contains("element vanished", StringComparison.Ordinal));
        runtime = new(fixture.Plan.SourceFingerprint, after) { RollbackSucceeded = false, HasPreservation = false };
        receipt = Run(runtime);
        check("absent preservation monitor does not invent an element inventory", !runtime.Events.Contains("inventory") && receipt.CreatedElements.SequenceEqual(progress.Created));
        runtime = new(fixture.Plan.SourceFingerprint, after) { PreservationError = new IOException("verification failed") };
        receipt = Run(runtime, error: new OperationCanceledException("cancelled"));
        check("verification failure aggregates a cancellation into native uncertainty", receipt.State == TimberNativeKitState.Unconfirmed && receipt.Diagnostics is [{ Code: "native_kit" }] && receipt.Diagnostics[0].Message.Contains("verification failed", StringComparison.Ordinal));
        bool escaped = false;
        try { Run(new(fixture.Plan.SourceFingerprint, after) { PreservationError = new OutOfMemoryException("oom") }); } catch (OutOfMemoryException) { escaped = true; }
        check("out of memory during verification is never converted to an ordinary receipt", escaped);
        escaped = false;
        try { Run(new(fixture.Plan.SourceFingerprint, after) { CommittedError = new IOException("status failed") }); } catch (IOException error) { escaped = error.Message == "status failed"; }
        check("status-read failure retains the original escape behavior", escaped);
    }

    private sealed class Runtime(string fingerprint, Target after) : INativeTimberKitRecoveryRuntime
    {
        internal List<string> Events { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];
        internal bool? PreservationCommitted { get; private set; }
        internal Target? FingerprintTarget { get; private set; }
        internal int? OwnedCount { get; private set; }
        public bool HasGroup { get; init; } = true;
        public bool GroupCommitted { get; init; }
        public bool GroupStarted { get; init; } = true;
        public bool GroupRolledBack { get; init; }
        public bool HasPreservation { get; init; } = true;
        public bool SourceActive { get; init; } = true;
        internal bool RollbackSucceeded { get; init; } = true;
        internal string FingerprintValue { get; init; } = fingerprint;
        internal Exception? CommittedError { get; init; }
        internal Exception? PreservationError { get; init; }
        internal Exception? ActiveError { get; init; }
        internal Exception? CaptureError { get; init; }
        internal Exception? RollbackError { get; init; }
        internal Exception? InventoryError { get; init; }
        internal Exception? UniqueIdError { get; init; }
        bool INativeTimberKitRecoveryRuntime.GroupCommitted { get { Events.Add("committed"); Throw(CommittedError); return GroupCommitted; } }
        bool INativeTimberKitRecoveryRuntime.GroupStarted { get { Events.Add("started"); return GroupStarted; } }
        bool INativeTimberKitRecoveryRuntime.GroupRolledBack { get { Events.Add("rolled_back"); return GroupRolledBack; } }
        bool INativeTimberKitRecoveryRuntime.SourceActive { get { Events.Add("active"); Throw(ActiveError); return SourceActive; } }
        public bool RollBack() { Events.Add("rollback"); Throw(RollbackError); return RollbackSucceeded; }
        public void RequireUnchanged(bool committed, CancellationToken cancellationToken)
        { Events.Add("preserve"); PreservationCommitted = committed; Tokens.Add(cancellationToken); Throw(PreservationError); }
        public string Fingerprint(TimberNativeKitPlan plan, Target target, IReadOnlyCollection<TimberNativeElement> owned, CancellationToken cancellationToken)
        { Events.Add("fingerprint"); FingerprintTarget = target; OwnedCount = owned.Count; Tokens.Add(cancellationToken); return FingerprintValue; }
        public Target CaptureTarget() { Events.Add("capture"); Throw(CaptureError); return after; }
        public IEnumerable<long> BeforeIds => [1, 2];
        public IEnumerable<long> CurrentIds { get { Events.Add("inventory"); Throw(InventoryError); return [1, 2, 3, 5]; } }
        public string UniqueId(long id) { Throw(UniqueIdError); return "unique-" + id; }
        private static void Throw(Exception? error) { if (error is not null) throw error; }
    }
}
