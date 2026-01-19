using TemsACC.Models;

namespace TemsACC.Collectors;

public interface ISystemInfoCollector
{
    Task<SystemProperties> CollectSystemPropertiesAsync();
    Task<MetricsSample> CollectMetricsAsync();
}