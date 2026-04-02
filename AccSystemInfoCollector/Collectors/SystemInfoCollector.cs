using Microsoft.Extensions.Logging;
using TEMS.ACC.Collectors.Readers;
using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors;

public sealed class SystemInfoCollector(ILogger<SystemInfoCollector> logger) : ISystemInfoCollector
{
    private readonly ISystemPropertiesReader _reader = SystemPropertiesReaderFactory.Create();

    public Task<Result<SystemProperties>> CollectSystemPropertiesAsync() =>
        Result<SystemProperties>.From(async () =>
        {
            logger.LogInformation("Collecting system properties on {OS}...",
                OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux");

            var p = new SystemProperties();

            p.SerialNumber = (await _reader.GetSerialNumberAsync()).GetValueOrDefault("Unknown");
            p.Uuid = (await _reader.GetUuidAsync()).GetValueOrDefault("Unknown");
            p.Hostname = _reader.GetHostname().GetValueOrDefault("Unknown");
            p.MacAddresses = _reader.GetMacAddresses().GetValueOrDefault([]);

            var cpu = (await _reader.GetCpuInfoAsync()).GetValueOrDefault(("Unknown", "Unknown", 0, 0, "Unknown", 0, 0));
            p.CpuManufacturer = cpu.Manufacturer;
            p.CpuModel = cpu.Model;
            p.CpuCores = cpu.Cores;
            p.CpuLogicalProcessors = cpu.LogicalProcessors;
            p.CpuArchitecture = cpu.Architecture;
            p.CpuMaxFrequencyGhz = cpu.MaxGhz;
            p.CpuMinFrequencyGhz = cpu.MinGhz;

            p.RamTotalGb = (await _reader.GetRamTotalGbAsync()).GetValueOrDefault(0);
            p.RamSlots = (await _reader.GetRamSlotsAsync()).GetValueOrDefault([]);
            var slots = (await _reader.GetRamSlotCountAsync()).GetValueOrDefault((0, 0));
            p.RamSlotsTotal = slots.Total;
            p.RamSlotsUsed = slots.Used;

            p.Drives = (await _reader.GetStorageDrivesAsync()).GetValueOrDefault([]);
            p.Gpus = (await _reader.GetGpusAsync()).GetValueOrDefault([]);
            p.NetworkAdapters = (await _reader.GetNetworkAdaptersAsync()).GetValueOrDefault([]);

            var os = (await _reader.GetOsInfoAsync()).GetValueOrDefault(("Unknown", "Unknown", DateTime.UtcNow, "Unknown"));
            p.OsName = os.Name;
            p.OsVersion = os.Version;
            p.LastBootTime = os.LastBoot;
            p.LastLoggedInUser = os.LastUser;
            p.Uptime = DateTime.UtcNow - os.LastBoot;

            var sw = (await _reader.GetSoftwareInfoAsync()).GetValueOrDefault(("Unknown", "Unknown", "Unknown", "Unknown", 0));
            p.Shell = sw.Shell;
            p.DisplayServer = sw.DisplayServer;
            p.DesktopEnvironment = sw.DesktopEnv;
            p.Locale = sw.Locale;
            p.InstalledPackagesCount = sw.PackageCount;

            var sys = (await _reader.GetSystemInfoAsync()).GetValueOrDefault(("Unknown", "Unknown", "Unknown", "Unknown"));
            p.SystemManufacturer = sys.Manufacturer;
            p.SystemModel = sys.Model;
            p.BiosVersion = sys.BiosVersion;
            p.Motherboard = sys.Motherboard;

            p.Security = (await _reader.GetSecurityInfoAsync()).GetValueOrDefault(new SecurityInfo());
            p.Virtualization = (await _reader.GetVirtualizationInfoAsync()).GetValueOrDefault(new VirtualizationInfo());
            p.Displays = (await _reader.GetDisplaysAsync()).GetValueOrDefault([]);
            p.CollectedAt = DateTime.UtcNow;

            LogProperties(p);
            return p;
        });

    public Result<MetricsSample> CollectMetricsSample() =>
        Result<MetricsSample>.From(() =>
        {
            var (netSent, netRecv) = _reader.GetNetworkBytes();
            var (battHealth, battCycles) = _reader.GetBatteryInfo();
            var (loadM1, loadM5, loadM15) = _reader.GetLoadAverage();
            var (diskRead, diskWrite) = _reader.GetDiskIo();
            var (gpuUsage, gpuTemp) = _reader.GetGpuMetrics();

            return new MetricsSample
            {
                Timestamp = DateTime.UtcNow,
                CpuLoadPercent = _reader.GetCpuLoadPercent(),
                CpuPerCorePercent = _reader.GetPerCoreCpuPercent(),
                CpuTemperatureCelsius = _reader.GetCpuTemperature(),
                LoadAverage1m = loadM1,
                LoadAverage5m = loadM5,
                LoadAverage15m = loadM15,
                RamUsedGb = _reader.GetRamUsedGb(),
                SwapUsedGb = _reader.GetSwapUsedGb(),
                DiskFreeGb = _reader.GetDiskFreeGb(),
                DiskReadMbps = diskRead,
                DiskWriteMbps = diskWrite,
                NetworkBytesSent = netSent,
                NetworkBytesReceived = netRecv,
                ActiveNetworkConnections = _reader.GetActiveNetworkConnections(),
                GpuUsagePercent = gpuUsage,
                GpuTemperatureCelsius = gpuTemp,
                BatteryHealthPercent = battHealth,
                BatteryCycleCount = battCycles,
                TopCpuProcesses = _reader.GetTopCpuProcesses(),
                TopRamProcesses = _reader.GetTopRamProcesses()
            };
        });

    private void LogProperties(SystemProperties p)
    {
        logger.LogInformation("""
            ╔══════════════════════════════════════════════════╗
            ║           TEMS · System Properties               ║
            ╠══ IDENTITY ══════════════════════════════════════╣
            ║  HOST        {Hostname,-38}║
            ║  SERIAL      {Serial,-38}║
            ║  UUID        {Uuid,-38}║
            ╠══ CPU ═══════════════════════════════════════════╣
            ║  MODEL       {CpuModel,-38}║
            ║  VENDOR      {CpuMfr,-38}║
            ║  CORES       {Cores,-38}║
            ║  LOGICAL     {Logical,-38}║
            ║  ARCH        {Arch,-38}║
            ║  FREQ        {Freq,-38}║
            ╠══ RAM ═══════════════════════════════════════════╣
            ║  TOTAL       {Ram,-38}║
            ║  SLOTS       {Slots,-38}║
            ╠══ OS ════════════════════════════════════════════╣
            ║  NAME        {OsName,-38}║
            ║  KERNEL      {OsVer,-38}║
            ║  LAST BOOT   {LastBoot,-38}║
            ║  UPTIME      {Uptime,-38}║
            ║  LAST USER   {LastUser,-38}║
            ║  SHELL       {Shell,-38}║
            ║  DISPLAY     {DisplayServer,-38}║
            ║  PACKAGES    {Packages,-38}║
            ╠══ BOARD ═════════════════════════════════════════╣
            ║  SYSTEM      {SysModel,-38}║
            ║  BIOS        {Bios,-38}║
            ║  BOARD       {Board,-38}║
            ╠══ SECURITY ══════════════════════════════════════╣
            ║  SECURE BOOT {SecureBoot,-38}║
            ║  TPM         {Tpm,-38}║
            ║  ENCRYPTION  {Encryption,-38}║
            ║  FIREWALL    {Firewall,-38}║
            ║  OPEN PORTS  {OpenPorts,-38}║
            ╠══ VIRTUALIZATION ════════════════════════════════╣
            ║  IS VM       {IsVm,-38}║
            ║  HYPERVISOR  {Hypervisor,-38}║
            ╚══════════════════════════════════════════════════╝
            """,
            p.Hostname, p.SerialNumber, p.Uuid,
            p.CpuModel, p.CpuManufacturer,
            $"{p.CpuCores} cores", $"{p.CpuLogicalProcessors} logical", p.CpuArchitecture,
            $"{p.CpuMinFrequencyGhz} – {p.CpuMaxFrequencyGhz} GHz",
            $"{p.RamTotalGb} GB", $"{p.RamSlotsUsed}/{p.RamSlotsTotal} used",
            p.OsName, p.OsVersion,
            p.LastBootTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            $"{(int)p.Uptime.TotalHours}h {p.Uptime.Minutes}m",
            p.LastLoggedInUser, p.Shell, p.DisplayServer,
            p.InstalledPackagesCount.ToString(),
            p.SystemModel, p.BiosVersion, p.Motherboard,
            p.Security.SecureBootEnabled ? "Enabled" : "Disabled",
            p.Security.TpmVersion,
            p.Security.DiskEncryptionEnabled ? "Active" : "None",
            p.Security.FirewallActive ? "Active" : "Inactive",
            string.Join(", ", p.Security.OpenPorts.Take(10)),
            p.Virtualization.IsVirtualMachine ? "Yes" : "No",
            p.Virtualization.HypervisorType);

        foreach (var d in p.Drives)
            logger.LogInformation("  💾 {Model} · {Size} GB · {Type}", d.Model, d.SizeGb, d.Type);
        foreach (var g in p.Gpus)
            logger.LogInformation("  🎮 {Model} · {Vram} MB VRAM", g.Model, g.VramMb);
        foreach (var slot in p.RamSlots)
            logger.LogInformation("  🧠 {Loc} · {Size} GB · {Type} {Speed} MHz · {Mfr}",
                slot.Locator, slot.SizeGb, slot.Type, slot.SpeedMhz, slot.Manufacturer);
        foreach (var n in p.NetworkAdapters.Where(a => a.IsUp && a.Type != "Loopback"))
            logger.LogInformation("  🌐 {Name} [{Type}] · {Speed} Mbps · {Ips}",
                n.Name, n.Type, n.SpeedMbps, string.Join(", ", n.IpAddresses.Take(2)));
    }
}