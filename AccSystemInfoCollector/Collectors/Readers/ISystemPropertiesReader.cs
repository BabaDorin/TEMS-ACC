using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors.Readers;

public interface ISystemPropertiesReader
{
    //Static properties
    Task<string> GetSerialNumberAsync();
    Task<string> GetUuidAsync();
    string GetHostname();
    List<string> GetMacAddresses();

    Task<(string Manufacturer, string Model, int Cores, int LogicalProcessors, string Architecture, double MaxGhz, double MinGhz)> GetCpuInfoAsync();
    Task<double> GetRamTotalGbAsync();
    Task<List<RamSlot>> GetRamSlotsAsync();
    Task<(int Total, int Used)> GetRamSlotCountAsync();
    Task<List<StorageDrive>> GetStorageDrivesAsync();
    Task<List<GpuInfo>> GetGpusAsync();
    Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync();
    Task<(string Name, string Version, DateTime LastBoot, string LastUser)> GetOsInfoAsync();
    Task<(string Shell, string DisplayServer, string DesktopEnv, string Locale, int PackageCount)> GetSoftwareInfoAsync();
    Task<(string Manufacturer, string Model, string BiosVersion, string Motherboard)> GetSystemInfoAsync();
    Task<List<DisplayInfo>> GetDisplaysAsync();
    Task<SecurityInfo> GetSecurityInfoAsync();
    Task<VirtualizationInfo> GetVirtualizationInfoAsync();

    //Metrics
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