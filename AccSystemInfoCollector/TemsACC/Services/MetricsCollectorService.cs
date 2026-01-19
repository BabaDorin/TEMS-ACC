using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TemsACC.ApiClient;
using TemsACC.Collectors;
using TemsACC.Models;
using TemsACC.Storage;

namespace TemsACC.Services;

public class MetricsCollectorService : BackgroundService
{
    private readonly ILogger<MetricsCollectorService> _logger;
    private readonly ISystemInfoCollector _systemInfo;
    private readonly IMetricsStore _metricsStore;
    private readonly ITemsApiClient _apiClient;
    private readonly TimeSpan _sampleInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _batchInterval = TimeSpan.FromMinutes(30);

    public MetricsCollectorService(
        ILogger<MetricsCollectorService> logger,
        ISystemInfoCollector systemInfo,
        IMetricsStore metricsStore,
        ITemsApiClient apiClient)
    {
        _logger = logger;
        _systemInfo = systemInfo;
        _metricsStore = metricsStore;
        _apiClient = apiClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics Collector Service started");
        _logger.LogInformation("Sampling every {Seconds} seconds, sending batches every {Minutes} minutes",
            _sampleInterval.TotalSeconds, _batchInterval.TotalMinutes);

        // Start both tasks in parallel
        var samplingTask = CollectMetricsSamples(stoppingToken);
        var batchTask = SendMetricsBatches(stoppingToken);

        await Task.WhenAll(samplingTask, batchTask);

        _logger.LogInformation("Metrics Collector Service stopped");
    }

    private async Task CollectMetricsSamples(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting metrics sampling...");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var metrics = await _systemInfo.CollectMetricsAsync();
                _metricsStore.AddSample(metrics);
                
                _logger.LogDebug(
                    "Metrics: CPU={Cpu}% | RAM={Ram}GB used | Disk={Disk}GB free | Net: ↑{Sent}B ↓{Recv}B | Battery={Battery}% ({Cycles} cycles)",
                    metrics.CpuLoadPercent,
                    metrics.RamUsedGb,
                    metrics.DiskFreeGb,
                    metrics.NetworkBytesSent,
                    metrics.NetworkBytesReceived,
                    metrics.BatteryHealthPercent,
                    metrics.BatteryCycleCount
                );

                await Task.Delay(_sampleInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Metrics sampling cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect metrics sample, will retry");
                
                try
                {
                    await Task.Delay(_sampleInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task SendMetricsBatches(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting metrics batch sending...");
        
        // Wait for first batch interval before sending
        await Task.Delay(_batchInterval, stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var samples = _metricsStore.GetAndClearSamples();
                
                if (samples.Count == 0)
                {
                    _logger.LogDebug("No metrics samples to send, skipping this batch");
                }
                else
                {
                    _logger.LogInformation("Preparing to send metrics batch with {Count} samples", samples.Count);

                    var batch = new MetricsBatch
                    {
                        BatchStart = samples.First().Timestamp,
                        BatchEnd = samples.Last().Timestamp,
                        Samples = samples
                    };

                    await _apiClient.SendMetricsAsync(batch);
                    _logger.LogInformation("Metrics batch sent successfully ({Count} samples)", samples.Count);
                }

                await Task.Delay(_batchInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Metrics batch sending cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send metrics batch, samples will be included in next batch");
                
                try
                {
                    await Task.Delay(_batchInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}