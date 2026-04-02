using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors;

public interface ISystemInfoCollector
{
    Task<Result<SystemProperties>> CollectSystemPropertiesAsync();
    Result<MetricsSample> CollectMetricsSample();
}