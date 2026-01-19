using TemsACC.Models;

namespace TemsACC.Storage;

public class InMemoryMetricsStore : IMetricsStore
{
    private readonly List<MetricsSample> _samples = new();
    private readonly object _lock = new();

    public void AddSample(MetricsSample sample)
    {
        lock (_lock)
        {
            _samples.Add(sample);
        }
    }

    public List<MetricsSample> GetAndClearSamples()
    {
        lock (_lock)
        {
            var samples = new List<MetricsSample>(_samples);
            _samples.Clear();
            return samples;
        }
    }
}