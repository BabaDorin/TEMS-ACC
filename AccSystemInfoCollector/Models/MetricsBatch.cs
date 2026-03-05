namespace TEMS.ACC.Models;

public sealed class MetricsBatch
{
    public DateTime BatchStart { get; set; }
    public DateTime BatchEnd { get; set; }
    public List<MetricsSample> Samples { get; set; } = [];
}