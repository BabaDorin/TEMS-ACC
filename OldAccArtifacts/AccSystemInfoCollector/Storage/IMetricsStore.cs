using TEMS.ACC.Models;

namespace TEMS.ACC.Storage;

public interface IMetricsStore
{
    void AddSample(MetricsSample sample);
    List<MetricsSample> GetAndClearSamples();
    int Count { get; }
}