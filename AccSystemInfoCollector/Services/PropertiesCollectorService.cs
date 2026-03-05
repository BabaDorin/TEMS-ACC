using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TEMS.ACC.ApiClient;
using TEMS.ACC.Collectors;
using TEMS.ACC.Models;

namespace TEMS.ACC.Services;

public sealed class PropertiesCollectorService(
    ILogger<PropertiesCollectorService> logger,
    ISystemInfoCollector collector,
    ITemsApiClient apiClient,
    IOptions<TemsConfiguration> config) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(config.Value.PropertiesIntervalHours);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PropertiesCollectorService started. Interval: {Interval}h", _interval.TotalHours);

        // Collect immediately on startup
        await CollectAndSendAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            await CollectAndSendAsync(stoppingToken);
    }

    private async Task CollectAndSendAsync(CancellationToken ct)
    {
        try
        {
            var properties = await collector.CollectSystemPropertiesAsync();
            var result = await apiClient.SendPropertiesAsync(properties);

            if (result.IsFailure)
                logger.LogWarning("Properties send failed: {Error}", result.Error);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in PropertiesCollectorService");
        }
    }
}