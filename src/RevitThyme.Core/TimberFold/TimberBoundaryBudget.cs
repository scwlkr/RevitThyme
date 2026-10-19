namespace RevitThyme.Core;

// A sum of independently proved displacement bounds remains a conservative bound
// across exposure, growth and sequential joints on the same original panel.
internal sealed class TimberBoundaryBudget
{
    private readonly Dictionary<string, long> accumulated = new(StringComparer.Ordinal);
    internal void Charge(string panelId, TimberBoundaryRefinement refinement)
    {
        accumulated.TryGetValue(panelId, out long previous);
        long total = checked(previous + refinement.MaximumDisplacementGridUnits);
        if (total > 500) throw new TimberPrismUnresolved("grid_refinement_budget", "Cumulative panel boundary refinement exceeds the reserved 0.000005 inch contour budget.");
        accumulated[panelId] = total;
    }
}
