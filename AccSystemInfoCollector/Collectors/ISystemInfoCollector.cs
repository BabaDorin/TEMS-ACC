using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors;

public interface ISystemInfoCollector
{
    Task<SystemProperties> CollectSystemPropertiesAsync();
    MetricsSample CollectMetricsSample();
}