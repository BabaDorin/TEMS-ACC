using System.Diagnostics;
using System.Net.NetworkInformation;
using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors.Readers;

public sealed class MacOsPropertiesReader : BasePropertiesReader, ISystemPropertiesReader
{
    // Static properties

    public async Task<string> GetSerialNumberAsync()
    {
        var output = await ExecuteCommandAsync("system_profiler", "SPHardwareDataType");
        return output.Split('\n').FirstOrDefault(l => l.Contains("Serial Number"))?.Split(':').Last().Trim() ?? "Unknown";
    }

    public async Task<string> GetUuidAsync()
    {
        var output = await ExecuteCommandAsync("system_profiler", "SPHardwareDataType");
        return output.Split('\n').FirstOrDefault(l => l.Contains("Hardware UUID") || l.Contains("Provisioning UDID"))?.Split(':').Last().Trim() ?? "Unknown";
    }

    public string GetHostname() => GetHostnameBase();
    public List<string> GetMacAddresses() => GetMacAddressesBase();

    public async Task<(string Manufacturer, string Model, int Cores, int LogicalProcessors, string Architecture, double MaxGhz, double MinGhz)> GetCpuInfoAsync()
    {
        var hw = await ExecuteCommandAsync("system_profiler", "SPHardwareDataType");
        var lines = hw.Split('\n');

        var chip = lines.FirstOrDefault(l => l.Contains("Chip:") || l.Contains("Processor Name:"))?.Split(':').Last().Trim() ?? "Unknown";
        var coreStr = lines.FirstOrDefault(l => l.Contains("Total Number of Cores:"))?.Split(':').Last().Trim() ?? "0";
        int.TryParse(coreStr.Split(' ')[0], out var cores);

        var speedStr = lines.FirstOrDefault(l => l.Contains("Processor Speed:"))?.Split(':').Last().Trim() ?? "";
        double maxGhz = 0;
        if (speedStr.Contains("GHz") && double.TryParse(speedStr.Replace("GHz", "").Trim(),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ghz))
            maxGhz = ghz;

        var arch = (await ExecuteCommandAsync("uname", "-m")).Trim();
        return ("Apple", chip, cores, cores, arch, maxGhz, 0);
    }

    public async Task<double> GetRamTotalGbAsync()
    {
        var hw = await ExecuteCommandAsync("system_profiler", "SPHardwareDataType");
        var memLine = hw.Split('\n').FirstOrDefault(l => l.Contains("Memory:"))?.Split(':').Last().Trim() ?? "0 GB";
        var parts = memLine.Split(' ');
        if (double.TryParse(parts[0], out var val))
        {
            if (parts.Length > 1 && parts[1].Equals("GB", StringComparison.OrdinalIgnoreCase)) return val;
            if (parts.Length > 1 && parts[1].Equals("MB", StringComparison.OrdinalIgnoreCase)) return Math.Round(val / 1024.0, 2);
        }
        return 0;
    }

    public async Task<List<RamSlot>> GetRamSlotsAsync()
    {
        var slots = new List<RamSlot>();
        var output = await ExecuteCommandAsync("system_profiler", "SPMemoryDataType");

        foreach (var block in output.Split("\n\n"))
        {
            if (!block.Contains("Size:")) continue;
            var lines = block.Split('\n');
            string Get(string key) => lines.FirstOrDefault(l => l.TrimStart().StartsWith(key))?.Split(':').Last().Trim() ?? "Unknown";

            var sizeStr = Get("Size:");
            if (sizeStr is "Empty" or "Unknown") continue;

            double sizeGb = 0;
            if (sizeStr.EndsWith("GB") && double.TryParse(sizeStr.Replace("GB", "").Trim(), out var g)) sizeGb = g;
            else if (sizeStr.EndsWith("MB") && double.TryParse(sizeStr.Replace("MB", "").Trim(), out var m)) sizeGb = Math.Round(m / 1024.0, 2);

            int.TryParse(System.Text.RegularExpressions.Regex.Match(Get("Speed:"), @"\d+").Value, out var speed);

            slots.Add(new RamSlot
            {
                Locator      = Get("BANK"),
                SizeGb       = sizeGb,
                Type         = Get("Type:"),
                SpeedMhz     = speed,
                Manufacturer = Get("Manufacturer:"),
                PartNumber   = Get("Part Number:")
            });
        }

        return slots;
    }

    public async Task<(int Total, int Used)> GetRamSlotCountAsync()
    {
        var output = await ExecuteCommandAsync("system_profiler", "SPMemoryDataType");
        var total = System.Text.RegularExpressions.Regex.Matches(output, "BANK").Count;
        var used  = output.Split("\n\n").Count(b => b.Contains("Size:") && !b.Contains("Empty"));
        return (total > 0 ? total : used, used);
    }

    public async Task<List<StorageDrive>> GetStorageDrivesAsync()
    {
        var drives = new List<StorageDrive>();
        var output = await ExecuteCommandAsync("system_profiler", "SPStorageDataType");

        foreach (var block in output.Split("\n\n"))
        {
            if (!block.Contains("Capacity:")) continue;
            var lines = block.Split('\n');
            string Get(string key) => lines.FirstOrDefault(l => l.TrimStart().StartsWith(key))?.Split(':').Last().Trim() ?? "Unknown";

            var medium = Get("Medium Type:");
            var type = medium.Contains("SSD") || medium.Contains("Flash") ? "SSD"
                     : medium.Contains("HDD") || medium.Contains("Rotational") ? "HDD"
                     : "SSD"; // Apple Silicon default

            drives.Add(new StorageDrive
            {
                Model  = Get("Volume Name:").NullIfEmpty() ?? Get("Physical Drive:"),
                SizeGb = ParseSizeToGb(Get("Capacity:")),
                Type   = type
            });
        }

        return drives;
    }

    public async Task<List<GpuInfo>> GetGpusAsync()
    {
        var gpus = new List<GpuInfo>();
        var output = await ExecuteCommandAsync("system_profiler", "SPDisplaysDataType");

        foreach (var block in output.Split("\n\n"))
        {
            if (!block.Contains("Chipset Model:")) continue;
            var lines = block.Split('\n');
            var model   = lines.FirstOrDefault(l => l.Contains("Chipset Model:"))?.Split(':').Last().Trim() ?? "Unknown";
            var vramStr = lines.FirstOrDefault(l => l.Contains("VRAM"))?.Split(':').Last().Trim() ?? "0 MB";
            gpus.Add(new GpuInfo { Model = model, VramMb = ParseVramToMb(vramStr) });
        }

        return gpus;
    }

    public async Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync()
    {
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            var ips = iface.GetIPProperties().UnicastAddresses.Select(a => a.Address.ToString()).ToList();

            var type = iface.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet     => "Ethernet",
                NetworkInterfaceType.Wireless80211 => "WiFi",
                NetworkInterfaceType.Loopback     => "Loopback",
                _ => iface.NetworkInterfaceType.ToString()
            };

            adapters.Add(new NetworkAdapterInfo
            {
                Name        = iface.Name,
                MacAddress  = mac,
                IpAddresses = ips,
                Type        = type,
                SpeedMbps   = iface.Speed > 0 ? iface.Speed / 1_000_000 : 0,
                Driver      = "Unknown",
                IsUp        = iface.OperationalStatus == OperationalStatus.Up
            });
        }

        return adapters;
    }

    public async Task<(string Name, string Version, DateTime LastBoot, string LastUser)> GetOsInfoAsync()
    {
        var sw = await ExecuteCommandAsync("sw_vers", "");
        var name    = sw.Split('\n').FirstOrDefault(l => l.StartsWith("ProductName:"))?.Split(':').Last().Trim() ?? "macOS";
        var version = sw.Split('\n').FirstOrDefault(l => l.StartsWith("ProductVersion:"))?.Split(':').Last().Trim() ?? "Unknown";

        var bootOutput = await ExecuteCommandAsync("sysctl", "-n kern.boottime");
        var epochMatch = System.Text.RegularExpressions.Regex.Match(bootOutput, @"sec = (\d+)");
        var lastBoot = epochMatch.Success && long.TryParse(epochMatch.Groups[1].Value, out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime : DateTime.UtcNow;

        var lastUser = (await ExecuteCommandAsync("last", "-1")).Split('\n').FirstOrDefault()?.Split(' ').FirstOrDefault() ?? "Unknown";

        return ($"{name} {version}", version, lastBoot, lastUser);
    }

    public async Task<(string Shell, string DisplayServer, string DesktopEnv, string Locale, int PackageCount)> GetSoftwareInfoAsync()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL")?.Split('/').Last() ?? "Unknown";
        var locale = (await ExecuteCommandAsync("defaults", "read NSGlobalDomain AppleLocale")).Trim().NullIfEmpty() ?? "Unknown";

        // Package count: try brew, then macports
        int packages = 0;
        var brew = await ExecuteCommandAsync("brew", "list --formula");
        if (!string.IsNullOrWhiteSpace(brew))
            packages = brew.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
        else
        {
            var port = await ExecuteCommandAsync("port", "installed");
            packages = port.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
        }

        return (shell, "Quartz Compositor", "macOS Aqua", locale, packages);
    }

    public async Task<(string Manufacturer, string Model, string BiosVersion, string Motherboard)> GetSystemInfoAsync()
    {
        var hw = await ExecuteCommandAsync("system_profiler", "SPHardwareDataType");
        var lines = hw.Split('\n');

        var model   = lines.FirstOrDefault(l => l.Contains("Model Name:"))?.Split(':').Last().Trim() ?? "Unknown";
        var modelId = lines.FirstOrDefault(l => l.Contains("Model Identifier:"))?.Split(':').Last().Trim() ?? "Unknown";
        var boot    = lines.FirstOrDefault(l => l.Contains("Boot ROM Version:"))?.Split(':').Last().Trim() ?? "Unknown";

        return ("Apple", model, boot, modelId);
    }

    public async Task<List<DisplayInfo>> GetDisplaysAsync()
    {
        var displays = new List<DisplayInfo>();
        var output = await ExecuteCommandAsync("system_profiler", "SPDisplaysDataType");

        var first = true;
        foreach (var block in output.Split("\n\n"))
        {
            var resLine = block.Split('\n').FirstOrDefault(l => l.Contains("Resolution:"));
            if (resLine is null) continue;
            var res = resLine.Split(':').Last().Trim().Split('@')[0].Trim().Replace(" x ", "x");
            displays.Add(new DisplayInfo { Resolution = res, IsPrimary = first });
            first = false;
        }

        return displays;
    }

    public async Task<SecurityInfo> GetSecurityInfoAsync()
    {
        var security = new SecurityInfo();

        // Secure Boot (Apple Silicon / T2)
        var sbOutput = await ExecuteCommandAsync("system_profiler", "SPiBridgeDataType");
        security.SecureBootEnabled = sbOutput.Contains("Full Security") || sbOutput.Contains("Reduced Security");

        // TPM — Apple uses T2/Secure Enclave, not classic TPM
        security.TpmVersion = sbOutput.Contains("Apple T2") ? "T2 (Apple)" : "Secure Enclave";

        // FileVault (disk encryption)
        var fvOutput = await ExecuteCommandAsync("fdesetup", "status");
        security.DiskEncryptionEnabled = fvOutput.Contains("On");

        // Firewall
        var fwOutput = await ExecuteCommandAsync("defaults", "read /Library/Preferences/com.apple.alf globalstate");
        security.FirewallActive = fwOutput.Trim() is "1" or "2";

        security.AppArmorStatus = "N/A (macOS)";

        // Failed logins
        var failedOutput = await ExecuteCommandAsync("log", "show --last 24h --predicate 'eventMessage contains \"Failed\"' --style syslog");
        security.FailedLoginAttempts = failedOutput.Split('\n').Count(l => l.Contains("Failed password") || l.Contains("Invalid user"));

        // Open ports
        var lsofOutput = await ExecuteCommandAsync("lsof", "-nP -iTCP -sTCP:LISTEN");
        security.OpenPorts = lsofOutput.Split('\n')
            .Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @":(\d+)\s*\(").Groups[1].Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        // SSH keys
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        security.SshKeysPresent = Directory.Exists(sshDir) && Directory.GetFiles(sshDir, "*.pub").Length > 0;

        // Pending updates
        var updatesOutput = await ExecuteCommandAsync("softwareupdate", "-l");
        security.PendingSecurityUpdates = updatesOutput.Split('\n').Count(l => l.TrimStart().StartsWith("*"));

        return security;
    }

    public async Task<VirtualizationInfo> GetVirtualizationInfoAsync()
    {
        var info = new VirtualizationInfo();

        var sysctl = await ExecuteCommandAsync("sysctl", "-n kern.hv_vmm_present");
        if (sysctl.Trim() == "1")
        {
            info.IsVirtualMachine = true;
            // Try to identify hypervisor
            var model = (await ExecuteCommandAsync("system_profiler", "SPHardwareDataType"))
                .Split('\n').FirstOrDefault(l => l.Contains("Model Identifier:"))?.Split(':').Last().Trim() ?? "";
            info.HypervisorType = model.Contains("VMware") ? "VMware"
                : model.Contains("VirtualBox") ? "VirtualBox"
                : "Unknown Hypervisor";
        }

        // Docker
        if (File.Exists("/.dockerenv"))
        {
            info.IsContainer = true;
            info.HypervisorType = "Docker";
        }

        return info;
    }
    
    // Metrics
    
    public double GetCpuLoadPercent()
    {
        try
        {
            var output = ExecuteCommandAsync("bash", "-c \"top -l 1 -s 0 | grep 'CPU usage'\"").Result;
            var match = System.Text.RegularExpressions.Regex.Match(output, @"([\d.]+)% user.*?([\d.]+)% sys");
            if (match.Success)
            {
                double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var user);
                double.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sys);
                return Math.Round(user + sys, 2);
            }
        }
        catch { }
        return 0;
    }

    public List<double> GetPerCoreCpuPercent()
    {
        // macOS doesn't expose per-core via simple file — use iostat
        try
        {
            var output = ExecuteCommandAsync("bash", "-c \"iostat -c 2 | tail -1\"").Result;
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var user)
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sys))
                return [Math.Round(user + sys, 1)]; // aggregate only on macOS
        }
        catch { }
        return [];
    }

    public double GetCpuTemperature() => 0; // Requires IOKit / powermetrics (sudo)

    public (double m1, double m5, double m15) GetLoadAverage()
    {
        try
        {
            var output = ExecuteCommandAsync("sysctl", "-n vm.loadavg").Result;
            var match = System.Text.RegularExpressions.Regex.Match(output, @"{ ([\d.]+) ([\d.]+) ([\d.]+) }");
            if (match.Success)
            {
                double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m1);
                double.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m5);
                double.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m15);
                return (m1, m5, m15);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    public double GetRamUsedGb()
    {
        try
        {
            var output = ExecuteCommandAsync("vm_stat", "").Result;
            var lines = output.Split('\n');

            long Get(string key)
            {
                var line = lines.FirstOrDefault(l => l.Contains(key));
                return line is not null && long.TryParse(
                    System.Text.RegularExpressions.Regex.Match(line, @"\d+").Value, out var v) ? v : 0;
            }

            var pageSize = 4096L;
            var active   = Get("Pages active:");
            var wired    = Get("Pages wired down:");
            var compressed = Get("Pages occupied by compressor:");

            return Math.Round((active + wired + compressed) * pageSize / 1024.0 / 1024.0 / 1024.0, 2);
        }
        catch { return 0; }
    }

    public double GetSwapUsedGb()
    {
        try
        {
            var vm = ExecuteCommandAsync("sysctl", "-n vm.swapusage").Result;
            var match = System.Text.RegularExpressions.Regex.Match(vm, @"used = ([\d.]+)M");
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mb))
                return Math.Round(mb / 1024.0, 2);
        }
        catch { }
        return 0;
    }

    public double GetDiskFreeGb()
    {
        try { return Math.Round((double)new DriveInfo("/").AvailableFreeSpace / 1024 / 1024 / 1024, 2); }
        catch { return 0; }
    }

    private long _lastDiskRead, _lastDiskWrite;

    public (double ReadMbps, double WriteMbps) GetDiskIo()
    {
        try
        {
            var output = ExecuteCommandAsync("iostat", "-d -K disk0").Result;
            var line = output.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("KB"));
            if (line is null) return (0, 0);
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3)
            {
                long.TryParse(p[1], out var read);
                long.TryParse(p[2], out var write);
                var dr = Math.Round((double)(read  - _lastDiskRead)  / 1024, 2);
                var dw = Math.Round((double)(write - _lastDiskWrite) / 1024, 2);
                _lastDiskRead = read; _lastDiskWrite = write;
                return (Math.Max(0, dr), Math.Max(0, dw));
            }
        }
        catch { }
        return (0, 0);
    }

    private long _lastNetSent, _lastNetRecv;

    public (long Sent, long Received) GetNetworkBytes()
    {
        try
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            var sent = ifaces.Sum(n => n.GetIPv4Statistics().BytesSent);
            var recv = ifaces.Sum(n => n.GetIPv4Statistics().BytesReceived);
            var ds = sent - _lastNetSent; var dr = recv - _lastNetRecv;
            _lastNetSent = sent; _lastNetRecv = recv;
            return (ds, dr);
        }
        catch { return (0, 0); }
    }

    public int GetActiveNetworkConnections()
    {
        try { return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; }
        catch { return 0; }
    }

    public (double UsagePercent, double TemperatureCelsius) GetGpuMetrics()
    {
        // Requires sudo powermetrics — not available without elevated privileges
        return (0, 0);
    }

    public (double HealthPercent, int CycleCount) GetBatteryInfo()
    {
        try
        {
            var output = ExecuteCommandAsync("pmset", "-g batt").Result;
            var pctMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+)%;");
            if (!pctMatch.Success) return (0, 0);
            double.TryParse(pctMatch.Groups[1].Value, out var pct);

            var cycleOutput = ExecuteCommandAsync("system_profiler", "SPPowerDataType").Result;
            var cycleMatch = System.Text.RegularExpressions.Regex.Match(cycleOutput, @"Cycle Count: (\d+)");
            int.TryParse(cycleMatch.Groups[1].Value, out var cycles);

            return (pct, cycles);
        }
        catch { return (0, 0); }
    }

    public List<ProcessInfo> GetTopCpuProcesses(int count = 5) => GetTopProcesses(count, byCpu: true);
    public List<ProcessInfo> GetTopRamProcesses(int count = 5) => GetTopProcesses(count, byCpu: false);

    private static List<ProcessInfo> GetTopProcesses(int count, bool byCpu) =>
        Process.GetProcesses()
            .OrderByDescending(p => { try { return byCpu ? p.TotalProcessorTime.TotalMilliseconds : (double)p.WorkingSet64; } catch { return 0.0; } })
            .Take(count)
            .Select(p => { try { return new ProcessInfo { Name = p.ProcessName, RamMb = Math.Round((double)p.WorkingSet64 / 1024 / 1024, 1) }; } catch { return null!; } })
            .Where(p => p is not null)
            .ToList();

    //Helpers

    private static double ParseSizeToGb(string s)
    {
        s = s.Trim().ToUpperInvariant().Split('(')[0].Trim();
        if (s.Contains("TB") && double.TryParse(s.Replace("TB", "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)) return t * 1024;
        if (s.Contains("GB") && double.TryParse(s.Replace("GB", "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g)) return g;
        if (s.Contains("MB") && double.TryParse(s.Replace("MB", "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m)) return Math.Round(m / 1024.0, 2);
        return 0;
    }

    private static long ParseVramToMb(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.Contains("GB") && long.TryParse(s.Replace("GB", "").Trim(), out var g)) return g * 1024;
        if (s.Contains("MB") && long.TryParse(s.Replace("MB", "").Trim(), out var m)) return m;
        return 0;
    }
}