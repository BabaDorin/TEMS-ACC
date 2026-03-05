using Microsoft.Extensions.Logging;
using TEMS.ACC.Collectors.Readers;
using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors;

public sealed class SystemInfoCollector(ILogger<SystemInfoCollector> logger) : ISystemInfoCollector
{
    private readonly ISystemPropertiesReader _reader = SystemPropertiesReaderFactory.Create();

    public async Task<SystemProperties> CollectSystemPropertiesAsync()
    {
        logger.LogInformation("Collecting system properties on {OS}...",
            OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux");

        var p = new SystemProperties();

        p.SerialNumber  = await _reader.GetSerialNumberAsync();
        p.Uuid          = await _reader.GetUuidAsync();
        p.Hostname      = _reader.GetHostname();
        p.MacAddresses  = _reader.GetMacAddresses();

        var (cpuMfr, cpuModel, cores, logical, arch, maxGhz, minGhz) = await _reader.GetCpuInfoAsync();
        p.CpuManufacturer      = cpuMfr;
        p.CpuModel             = cpuModel;
        p.CpuCores             = cores;
        p.CpuLogicalProcessors = logical;
        p.CpuArchitecture      = arch;
        p.CpuMaxFrequencyGhz   = maxGhz;
        p.CpuMinFrequencyGhz   = minGhz;

        p.RamTotalGb  = await _reader.GetRamTotalGbAsync();
        p.RamSlots    = await _reader.GetRamSlotsAsync();
        var (ramTotal, ramUsed) = await _reader.GetRamSlotCountAsync();
        p.RamSlotsTotal = ramTotal;
        p.RamSlotsUsed  = ramUsed;

        p.Drives           = await _reader.GetStorageDrivesAsync();
        p.Gpus             = await _reader.GetGpusAsync();
        p.NetworkAdapters  = await _reader.GetNetworkAdaptersAsync();

        var (osName, osVer, lastBoot, lastUser) = await _reader.GetOsInfoAsync();
        p.OsName           = osName;
        p.OsVersion        = osVer;
        p.LastBootTime     = lastBoot;
        p.LastLoggedInUser = lastUser;
        p.Uptime           = DateTime.UtcNow - lastBoot;

        var (shell, displayServer, de, locale, packages) = await _reader.GetSoftwareInfoAsync();
        p.Shell                  = shell;
        p.DisplayServer          = displayServer;
        p.DesktopEnvironment     = de;
        p.Locale                 = locale;
        p.InstalledPackagesCount = packages;

        var (sysMfr, sysModel, bios, mobo) = await _reader.GetSystemInfoAsync();
        p.SystemManufacturer = sysMfr;
        p.SystemModel        = sysModel;
        p.BiosVersion        = bios;
        p.Motherboard        = mobo;

        p.Security        = await _reader.GetSecurityInfoAsync();
        p.Virtualization  = await _reader.GetVirtualizationInfoAsync();
        p.Displays        = await _reader.GetDisplaysAsync();
        p.CollectedAt     = DateTime.UtcNow;

        LogProperties(p);
        return p;
    }

    public MetricsSample CollectMetricsSample()
    {
        var (netSent, netRecv)     = _reader.GetNetworkBytes();
        var (battHealth, battCycles) = _reader.GetBatteryInfo();
        var (loadM1, loadM5, loadM15) = _reader.GetLoadAverage();
        var (diskRead, diskWrite)  = _reader.GetDiskIo();
        var (gpuUsage, gpuTemp)    = _reader.GetGpuMetrics();

        return new MetricsSample
        {
            Timestamp                = DateTime.UtcNow,
            CpuLoadPercent           = _reader.GetCpuLoadPercent(),
            CpuPerCorePercent        = _reader.GetPerCoreCpuPercent(),
            CpuTemperatureCelsius    = _reader.GetCpuTemperature(),
            LoadAverage1m            = loadM1,
            LoadAverage5m            = loadM5,
            LoadAverage15m           = loadM15,
            RamUsedGb                = _reader.GetRamUsedGb(),
            SwapUsedGb               = _reader.GetSwapUsedGb(),
            DiskFreeGb               = _reader.GetDiskFreeGb(),
            DiskReadMbps             = diskRead,
            DiskWriteMbps            = diskWrite,
            NetworkBytesSent         = netSent,
            NetworkBytesReceived     = netRecv,
            ActiveNetworkConnections = _reader.GetActiveNetworkConnections(),
            GpuUsagePercent          = gpuUsage,
            GpuTemperatureCelsius    = gpuTemp,
            BatteryHealthPercent     = battHealth,
            BatteryCycleCount        = battCycles,
            TopCpuProcesses          = _reader.GetTopCpuProcesses(),
            TopRamProcesses          = _reader.GetTopRamProcesses()
        };
    }

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
            ║  TIMEZONE    {Tz,-38}║
            ║  SHELL       {Shell,-38}║
            ║  DISPLAY     {DisplayServer,-38}║
            ║  DE          {De,-38}║
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
            ║  APPARMOR    {AppArmor,-38}║
            ║  OPEN PORTS  {OpenPorts,-38}║
            ║  FAILED LOGINS {FailedLogins,-36}║
            ╠══ VIRTUALIZATION ════════════════════════════════╣
            ║  IS VM       {IsVm,-38}║
            ║  HYPERVISOR  {Hypervisor,-38}║
            ╚══════════════════════════════════════════════════╝
            """,
            p.Hostname, p.SerialNumber, p.Uuid,
            p.CpuModel, p.CpuManufacturer,
            $"{p.CpuCores} cores", $"{p.CpuLogicalProcessors} logical", p.CpuArchitecture,
            $"{p.CpuMinFrequencyGhz} – {p.CpuMaxFrequencyGhz} GHz",
            $"{p.RamTotalGb} GB",
            $"{p.RamSlotsUsed}/{p.RamSlotsTotal} used",
            p.OsName, p.OsVersion,
            p.LastBootTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            $"{(int)p.Uptime.TotalHours}h {p.Uptime.Minutes}m",
            p.LastLoggedInUser, p.Timezone, p.Shell, p.DisplayServer, p.DesktopEnvironment,
            p.InstalledPackagesCount.ToString(),
            p.SystemModel, p.BiosVersion, p.Motherboard,
            p.Security.SecureBootEnabled ? "✓ Enabled" : "✗ Disabled",
            p.Security.TpmVersion,
            p.Security.DiskEncryptionEnabled ? "✓ LUKS Active" : "✗ None",
            p.Security.FirewallActive ? "✓ Active" : "✗ Inactive",
            p.Security.AppArmorStatus,
            string.Join(", ", p.Security.OpenPorts.Take(10)),
            p.Security.FailedLoginAttempts.ToString(),
            p.Virtualization.IsVirtualMachine ? "Yes" : "No",
            p.Virtualization.HypervisorType
        );

        foreach (var d in p.Drives)
            logger.LogInformation("  💾 {Model} · {Size} GB · {Type}", d.Model, d.SizeGb, d.Type);

        foreach (var g in p.Gpus)
            logger.LogInformation("  🎮 {Model} · {Vram} MB VRAM", g.Model, g.VramMb);

        foreach (var slot in p.RamSlots)
            logger.LogInformation("  🧠 {Locator} · {Size} GB · {Type} {Speed} MHz · {Mfr}",
                slot.Locator, slot.SizeGb, slot.Type, slot.SpeedMhz, slot.Manufacturer);

        foreach (var n in p.NetworkAdapters.Where(a => a.IsUp && a.Type != "Loopback"))
            logger.LogInformation("  🌐 {Name} [{Type}] · {Speed} Mbps · {Ips} · Driver: {Driver}",
                n.Name, n.Type, n.SpeedMbps, string.Join(", ", n.IpAddresses.Take(2)), n.Driver);
    }
}