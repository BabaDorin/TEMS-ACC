using TEMS.ACC.Models;

namespace TEMS.ACC.ApiClient;

public interface ITemsApiClient
{
    Task<Result<string>> SendPropertiesAsync(SystemProperties properties);
    Task<Result<string>> SendMetricsAsync(MetricsBatch batch);
    Task<Result<string>> SendPingAsync();
}