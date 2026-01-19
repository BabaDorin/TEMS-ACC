using TemsACC.Models;

namespace TemsACC.ApiClient;

public interface ITemsApiClient
{
    Task SendPropertiesAsync(SystemProperties properties);
    Task SendMetricsAsync(MetricsBatch batch);
    Task SendPingAsync();
}