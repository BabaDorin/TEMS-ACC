using System.Collections.Concurrent;
using TEMS.ACC.Models;

namespace TEMS.ACC.Storage;

public sealed class InMemoryMetricsStore : IMetricsStore
{
    private readonly ConcurrentBag<MetricsSample> _samples = new();

    public void AddSample(MetricsSample sample) => _samples.Add(sample);

    public int Count => _samples.Count;

    public List<MetricsSample> GetAndClearSamples()
    {
        var result = _samples.ToList();
        _samples.Clear();
        return result;
    }
}