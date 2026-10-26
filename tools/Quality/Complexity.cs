namespace RevitThyme.Quality;

public static class Complexity
{
    public static double Crap(int complexity, double coverage)
    {
        if (complexity < 1 || !double.IsFinite(coverage) || coverage is < 0 or > 1)
            throw new ArgumentException("Complexity must be positive and coverage must be a fraction in [0,1].");
        return complexity * complexity * Math.Pow(1 - coverage, 3) + complexity;
    }
}
