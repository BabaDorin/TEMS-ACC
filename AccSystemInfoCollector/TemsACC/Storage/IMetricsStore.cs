using TemsACC.Models;

namespace TemsACC.Storage;

public interface IMetricsStore
{
    void AddSample(MetricsSample sample);
    List<MetricsSample> GetAndClearSamples();
}