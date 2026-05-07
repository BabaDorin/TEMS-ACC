using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors.Readers;

public interface ISystemPropertiesReader
{
    //Static properties
    Task<Result<string>> GetSerialNumberAsync();
    Task<Result<string>> GetUuidAsync();
    Result<string> GetHostname();
    Result<List<string>> GetMacAddresses();

    Task<Result<(string Manufacturer, string Model, int Cores, int LogicalProcessors,
        string Architecture, double MaxGhz, double MinGhz)>> GetCpuInfoAsync();

    Task<Result<double>> GetRamTotalGbAsync();
    Task<Result<List<RamSlot>>> GetRamSlotsAsync();
    Task<Result<(int Total, int Used)>> GetRamSlotCountAsync();
    Task<Result<List<StorageDrive>>> GetStorageDrivesAsync();
    Task<Result<List<GpuInfo>>> GetGpusAsync();
    Task<Result<List<NetworkAdapterInfo>>> GetNetworkAdaptersAsync();

    Task<Result<(string Name, string Version, DateTime LastBoot, string LastUser)>> GetOsInfoAsync();

    Task<Result<(string Shell, string DisplayServer, string DesktopEnv,
        string Locale, int PackageCount)>> GetSoftwareInfoAsync();

    Task<Result<(string Manufacturer, string Model, string BiosVersion,
        string Motherboard)>> GetSystemInfoAsync();

    Task<Result<List<DisplayInfo>>> GetDisplaysAsync();
    Task<Result<SecurityInfo>> GetSecurityInfoAsync();
    Task<Result<VirtualizationInfo>> GetVirtualizationInfoAsync();

    // Metrics (plain — wrapped at collector level)
    double GetCpuLoadPercent();
    List<double> GetPerCoreCpuPercent();
    double GetCpuTemperature();
    (double m1, double m5, double m15) GetLoadAverage();
    double GetRamUsedGb();
    double GetSwapUsedGb();
    double GetDiskFreeGb();
    (double ReadMbps, double WriteMbps) GetDiskIo();
    (long Sent, long Received) GetNetworkBytes();
    int GetActiveNetworkConnections();
    (double UsagePercent, double TemperatureCelsius) GetGpuMetrics();
    (double HealthPercent, int CycleCount) GetBatteryInfo();
    List<ProcessInfo> GetTopCpuProcesses(int count = 5);
    List<ProcessInfo> GetTopRamProcesses(int count = 5);
}