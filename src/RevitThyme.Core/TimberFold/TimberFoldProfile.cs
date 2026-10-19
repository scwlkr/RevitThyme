namespace RevitThyme.Core;

// Dimensions and tolerances describe physical stock inches; extraction stays in Revit feet.
public sealed record TimberFoldProfile(
    double ScaleDenominator = 48,
    double StockWidthInches = 24,
    double StockHeightInches = 18,
    double ThicknessInches = 0.125,
    double MarginInches = 0.25,
    double GapInches = 0.125,
    double MinimumBarInches = 0.0625,
    double ChordToleranceInches = 0.001,
    double JointClearanceInches = 0.001,
    double RoofFitSearchInches = 0.5,
    bool ThicknessMeasured = false,
    bool StockMeasured = false,
    bool KerfConfirmed = false,
    bool GrainConfirmed = false,
    TimberRotation Rotation = TimberRotation.QuarterTurn,
    TimberGrain Grain = TimberGrain.None,
    TimberKerfOwnership KerfOwnership = TimberKerfOwnership.LaserSoftware)
{
    public TimberDiagnostic[] Validate()
    {
        var errors = new List<TimberDiagnostic>();
        Positive(nameof(ScaleDenominator), ScaleDenominator, 1000000);
        Positive(nameof(StockWidthInches), StockWidthInches, 1000000);
        Positive(nameof(StockHeightInches), StockHeightInches, 1000000);
        Positive(nameof(ThicknessInches), ThicknessInches, 1000000);
        Positive(nameof(MinimumBarInches), MinimumBarInches, 1000000);
        Positive(nameof(ChordToleranceInches), ChordToleranceInches, 0.1);
        Positive(nameof(RoofFitSearchInches), RoofFitSearchInches, 1000000);
        Nonnegative(nameof(MarginInches), MarginInches);
        Nonnegative(nameof(GapInches), GapInches);
        Nonnegative(nameof(JointClearanceInches), JointClearanceInches);
        if (double.IsFinite(StockWidthInches) && StockWidthInches > 0 && double.IsFinite(StockHeightInches) && StockHeightInches > 0 &&
            double.IsFinite(MarginInches) && MarginInches >= 0 && (2 * MarginInches >= StockWidthInches || 2 * MarginInches >= StockHeightInches))
            Error("stock_border", "The border must leave positive usable stock width and height.");
        if (!Enum.IsDefined(Rotation) || !Enum.IsDefined(Grain) || !Enum.IsDefined(KerfOwnership))
            Error("invalid_profile_choice", "Rotation, grain and kerf ownership must be explicit supported choices.");
        return errors.ToArray();

        void Positive(string name, double value, double maximum)
        {
            if (!double.IsFinite(value) || value <= 0 || value > maximum)
                Error("invalid_profile", $"{name} must be finite, positive and at most {maximum}.");
        }
        void Nonnegative(string name, double value)
        {
            if (!double.IsFinite(value) || value < 0 || value > 1000000)
                Error("invalid_profile", $"{name} must be finite and between zero and 1000000.");
        }
        void Error(string code, string message) => errors.Add(new(code, TimberSeverity.Error, message));
    }
}
