using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TEMS.ACC.ApiClient;
using TEMS.ACC.Collectors;
using TEMS.ACC.Models;
using TEMS.ACC.Services;
using TEMS.ACC.Storage;

var builder = Host.CreateApplicationBuilder(args);

//Configuration
builder.Services.Configure<TemsConfiguration>(
    builder.Configuration.GetSection(TemsConfiguration.SectionName));

//HTTP
builder.Services.AddHttpClient();

//Core services
builder.Services.AddSingleton<ISystemInfoCollector, SystemInfoCollector>();
builder.Services.AddSingleton<IMetricsStore, InMemoryMetricsStore>();
builder.Services.AddSingleton<ITemsApiClient, TemsApiClient>();

//Background workers
builder.Services.AddHostedService<PropertiesCollectorService>();
builder.Services.AddHostedService<MetricsCollectorService>();

//OS-specific hosting
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService(o => o.ServiceName = "TEMS ACC Worker");
else if (OperatingSystem.IsLinux())
    builder.Services.AddSystemd();

var host = builder.Build();
await host.RunAsync();