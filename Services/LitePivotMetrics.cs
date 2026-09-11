namespace Valtrans.Services;

/// <summary>Timing for one Lite batch. JP↔KO uses an English pivot and may need two model calls.</summary>
public sealed record LitePivotMetrics(
    string SourceLanguage,
    string TargetLanguage,
    bool UsedEnglishPivot,
    int ModelCalls,
    double PivotLegMs,
    double SecondLegMs,
    double TotalMs,
    int Lines,
    int CalloutShortCircuits);
