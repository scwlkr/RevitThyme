using RevitThyme.Addin;

namespace RevitThyme.Unit;

internal static class TimberFoldNativePreservation
{
    public static void Run(Action<string, bool> check)
    {
        var opaque = new NativeTimberPreservationStamp(713, "original-713", "same-content", "Autodesk.Revit.DB.Element", false, -1, -1, false, 0);
        var unsetZero = new NativeTimberParameterStamp(-7, "Double", false, "0");
        var unset = opaque with { ContentHash = NativeTimberKitPreservationCheck.ContentHash("", -1, [unsetZero]) };
        var assigned = opaque with { ContentHash = NativeTimberKitPreservationCheck.ContentHash("", -1, [unsetZero with { HasValue = true }]) };
        const string ownership = "{\"schema\":1,\"runId\":\"original\",\"previewRunId\":\"preview\",\"role\":\"NativeSheet\",\"uniqueId\":\"sheet\",\"contentHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";
        var originalTag = NativeTimberKitPreservationCheck.ContentHash("Sheet", 4, [], ownership);
        check("native content proof includes exact ownership role preview ID and content binding", new[] {
            ownership.Replace("NativeSheet", "CutView", StringComparison.Ordinal),
            ownership.Replace("preview\"", "different\"", StringComparison.Ordinal),
            ownership.Replace(new string('a', 64), new string('b', 64), StringComparison.Ordinal), null
        }.All(changed => NativeTimberKitPreservationCheck.ContentHash("Sheet", 4, [], changed) != originalTag));
        check("native preservation rejects numeric assignment-state drift even when both numeric reads return zero",
            !NativeTimberKitPreservationCheck.Check([unset], [assigned], [], [], []));
        check("native preservation allows only a per-run measured unchanged opaque bookkeeping notification",
            NativeTimberKitPreservationCheck.Check([opaque], [opaque], [713], [], [opaque]));
        check("native preservation rejects an uncalibrated original notification",
            !NativeTimberKitPreservationCheck.Check([opaque], [opaque], [713], [], []));
        check("native preservation never permits deletion of a calibrated original",
            !NativeTimberKitPreservationCheck.Check([opaque], [opaque], [], [713], [opaque]));
        check("native preservation rejects missing original content without a deletion notification",
            !NativeTimberKitPreservationCheck.Check([opaque], [], [], [], [opaque]));
        check("native preservation rejects changed content on a calibrated original",
            !NativeTimberKitPreservationCheck.Check([opaque], [opaque with { ContentHash = "changed" }], [713], [], [opaque]));
        check("native preservation rejects changed original identity",
            !NativeTimberKitPreservationCheck.Check([opaque], [opaque with { UniqueId = "replacement" }], [], [], []));
        foreach (var ineligible in new[] { opaque with { RuntimeType = "Autodesk.Revit.DB.ViewDrafting" }, opaque with { HasCategory = true },
            opaque with { TypeId = 3 }, opaque with { OwnerViewId = 5 }, opaque with { ViewSpecific = true }, opaque with { EntitySchemaCount = 1 } })
            check("native preservation rejects calibrated non-opaque shape " + ineligible,
                !NativeTimberKitPreservationCheck.Check([ineligible], [ineligible], [713], [], [ineligible]));
        check("native preservation rejects invented calibration identities",
            !NativeTimberKitPreservationCheck.Check([opaque], [opaque], [], [], [opaque with { Id = 900 }]));
        check("native preservation allows new owned documentation notifications alongside calibrated bookkeeping",
            NativeTimberKitPreservationCheck.Check([opaque], [opaque, opaque with { Id = 900, UniqueId = "new" }], [713, 900], [901], [opaque]));
    }
}
