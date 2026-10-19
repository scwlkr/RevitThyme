using System.Text.Json;

namespace RevitThyme.Core;

internal sealed record TimberStageTiming(string Stage, double Milliseconds);

public sealed partial class TimberRunCoordinator
{
    private static double? ValidMeasurement(double? milliseconds)
    {
        if (milliseconds is { } value && (!double.IsFinite(value) || value < 0))
            throw new InvalidDataException("Native stage duration is invalid.");
        return milliseconds;
    }

    private void WriteMeasurements(Entry entry, TimberRunSummary next)
    {
        double elapsed = next.ElapsedMilliseconds;
        if (entry.Summary.State != next.State)
        {
            entry.Stages.Add(new(entry.Summary.State.ToString(), Math.Max(0, elapsed - entry.StageStartedMilliseconds)));
            entry.StageStartedMilliseconds = elapsed;
        }
        var report = new
        {
            schema = 1, next.RunId, next.State, elapsedMilliseconds = elapsed,
            stages = entry.Stages.ToArray(), activeStage = next.Terminal ? null : next.State.ToString(),
            entry.StageStartedMilliseconds, entry.NativeExtractionMilliseconds, entry.NativeFinalizationMilliseconds, entry.NativeReconciliationMilliseconds,
            timingBasis = "Stage wall times include queue/scheduling and file IO. Native durations are measured inside API callbacks and overlap those stages; do not sum both groups."
        };
        store.WriteMeasurements(next.RunId, JsonSerializer.SerializeToUtf8Bytes(report, Protocol.Json));
    }
}
