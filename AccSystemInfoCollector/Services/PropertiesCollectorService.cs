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

        await CollectAndSendAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            await CollectAndSendAsync(stoppingToken);
    }

    private async Task CollectAndSendAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var collectResult = await collector.CollectSystemPropertiesAsync();

        if (collectResult.IsFailure)
        {
            logger.LogError("Failed to collect system properties: {Error}", collectResult.Error);
            return;
        }

        var sendResult = await apiClient.SendPropertiesAsync(collectResult.Value!);

        if (sendResult.IsFailure)
            logger.LogWarning("Failed to send properties: {Error}", sendResult.Error);
    }
}