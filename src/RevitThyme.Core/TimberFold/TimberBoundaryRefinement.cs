using System.Collections.Immutable;
using System.Globalization;

namespace RevitThyme.Core;

public sealed record TimberGridPoint(long X, long Y);
public sealed record TimberBoundaryRefinementPoint(TimberGridPoint OriginalEdgeA, TimberGridPoint OriginalEdgeB, TimberGridPoint InsertedPoint);
// Difference: refined = output + changed. Union: refined + changed = output.
public sealed record TimberBoundaryRefinement(string Operation, string OriginalTwiceGridArea, string SignedTwiceGridAdjustment,
    string RefinedTwiceGridArea, string OutputTwiceGridArea, string ChangedTwiceGridArea,
    long MaximumDisplacementGridUnits, ImmutableArray<TimberBoundaryRefinementPoint> Points)
{
    public double OriginalAreaSquareInches => Area(OriginalTwiceGridArea);
    public double RefinedAreaSquareInches => Area(RefinedTwiceGridArea);
    public double MaximumDisplacementInches => MaximumDisplacementGridUnits / TimberContours.Grid;
    private static double Area(string value) => (double)Int128.Parse(value, CultureInfo.InvariantCulture) / 2 / TimberContours.Grid / TimberContours.Grid;
}
