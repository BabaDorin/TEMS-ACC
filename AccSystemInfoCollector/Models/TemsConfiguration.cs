namespace TEMS.ACC.Models;

public sealed class TemsConfiguration
{
    public const string SectionName = "Tems";

    public string ApiUrl { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public int PropertiesIntervalHours { get; init; } = 48;
    public int MetricsSampleIntervalSeconds { get; init; } = 10;
    public int MetricsBatchIntervalMinutes { get; init; } = 30;
}