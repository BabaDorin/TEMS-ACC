using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TemsACC.ApiClient;
using TemsACC.Collectors;

namespace TemsACC.Services;

public class PropertiesCollectorService : BackgroundService
{
    private readonly ILogger<PropertiesCollectorService> _logger;
    private readonly ISystemInfoCollector _systemInfo;
    private readonly ITemsApiClient _apiClient;
    private readonly TimeSpan _collectionInterval = TimeSpan.FromHours(48);
    private readonly TimeSpan _pingInterval = TimeSpan.FromMinutes(30);

    public PropertiesCollectorService(
        ILogger<PropertiesCollectorService> logger,
        ISystemInfoCollector systemInfo,
        ITemsApiClient apiClient)
    {
        _logger = logger;
        _systemInfo = systemInfo;
        _apiClient = apiClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Properties Collector Service started");

        // Initial collection on startup
        await CollectAndSendProperties();

        // Start ping task in parallel
        var pingTask = SendPeriodicPings(stoppingToken);

        // Main collection loop - every 48 hours
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_collectionInterval, stoppingToken);
                await CollectAndSendProperties();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Properties collection cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in properties collection loop");
            }
        }

        // Wait for ping task to complete
        await pingTask;
        
        _logger.LogInformation("Properties Collector Service stopped");
    }

    private async Task CollectAndSendProperties()
    {
        try
        {
            _logger.LogInformation("Collecting system properties...");
            var properties = await _systemInfo.CollectSystemPropertiesAsync();

            _logger.LogInformation("Sending properties to TEMS API...");
            await _apiClient.SendPropertiesAsync(properties);

            _logger.LogInformation("Properties collection and submission completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect or send properties");
        }
    }

    private async Task SendPeriodicPings(CancellationToken stoppingToken)
    {
        // Wait initial delay before first ping
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _apiClient.SendPingAsync();
                _logger.LogDebug("Ping sent successfully");
                
                await Task.Delay(_pingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Ping task cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send ping, will retry in {Minutes} minutes", _pingInterval.TotalMinutes);
                
                try
                {
                    await Task.Delay(_pingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}