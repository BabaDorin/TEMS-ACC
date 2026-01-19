using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TemsACC.ApiClient;
using TemsACC.Collectors;
using TemsACC.Configuration;
using TemsACC.Services;
using TemsACC.Storage;

namespace TemsACC;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure as Windows Service / systemd daemon / macOS LaunchAgent
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "TEMS ACC Worker";
            });
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            builder.Services.AddSystemd();
        }

        // Register services
        builder.Services.AddSingleton<ConfigurationManager>();
        builder.Services.AddSingleton<ISystemInfoCollector, SystemInfoCollector>();
        builder.Services.AddSingleton<ITemsApiClient, TemsApiClient>();
        builder.Services.AddSingleton<IMetricsStore, InMemoryMetricsStore>();
        
        builder.Services.AddHostedService<PropertiesCollectorService>();
        builder.Services.AddHostedService<MetricsCollectorService>();
        
        builder.Services.AddHttpClient();

        var host = builder.Build();
        
        Console.WriteLine("TEMS ACC Service is starting...");
        
        await host.RunAsync();
    }
}