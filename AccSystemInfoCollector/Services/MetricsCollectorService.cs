using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TEMS.ACC.ApiClient;
using TEMS.ACC.Collectors;
using TEMS.ACC.Models;
using TEMS.ACC.Storage;

namespace TEMS.ACC.Services;

public sealed class MetricsCollectorService(
    ILogger<MetricsCollectorService> logger,
    ISystemInfoCollector collector,
    ITemsApiClient apiClient,
    IMetricsStore store,
    IOptions<TemsConfiguration> config) : BackgroundService
{
    private readonly TimeSpan _sampleInterval = TimeSpan.FromSeconds(config.Value.MetricsSampleIntervalSeconds);
    private readonly TimeSpan _batchInterval = TimeSpan.FromMinutes(config.Value.MetricsBatchIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MetricsCollectorService started. Sample: {S}s · Batch: {B}min",
            _sampleInterval.TotalSeconds, _batchInterval.TotalMinutes);

        await Task.WhenAll(
            RunSamplingLoopAsync(stoppingToken),
            RunBatchingLoopAsync(stoppingToken));
    }

    private async Task RunSamplingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_sampleInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            var result = collector.CollectMetricsSample();

            if (result.IsFailure)
            {
                logger.LogWarning("Failed to collect metrics sample: {Error}", result.Error);
                continue;
            }

            store.AddSample(result.Value!);

            logger.LogDebug(
                "📊 CPU {Cpu}% | RAM {Ram} GB | Disk {Disk} GB | NET ↑{Sent} ↓{Recv} B | GPU {Gpu}%",
                result.Value!.CpuLoadPercent, result.Value.RamUsedGb, result.Value.DiskFreeGb,
                result.Value.NetworkBytesSent, result.Value.NetworkBytesReceived, result.Value.GpuUsagePercent);
        }
    }

    private async Task RunBatchingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_batchInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            await SendBatchAsync();
    }

    private async Task SendBatchAsync()
    {
        var samples = store.GetAndClearSamples();
        if (samples.Count == 0) return;

        var batch = new MetricsBatch
        {
            BatchStart = samples.Min(s => s.Timestamp),
            BatchEnd = samples.Max(s => s.Timestamp),
            Samples = samples
        };

        logger.LogInformation("Sending batch of {Count} samples", batch.Samples.Count);

        var result = await apiClient.SendMetricsAsync(batch);

        if (result.IsSuccess)
        {
            logger.LogInformation("✓ Batch sent successfully.");
        }
        else
        {
            foreach (var s in samples) store.AddSample(s);
            logger.LogWarning("✗ Batch failed: {Error}. Samples retained ({Count}).", result.Error, samples.Count);
        }
    }
}