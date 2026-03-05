using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors.Readers;

public sealed class LinuxPropertiesReader : BasePropertiesReader, ISystemPropertiesReader
{
    // Static properties

    public async Task<string> GetSerialNumberAsync()
    {
        var v = (await ReadFileAsync("/sys/class/dmi/id/product_serial")).Trim();
        if (v.NullIfEmpty() is not null) return v;
        return (await ExecuteCommandAsync("dmidecode", "-s system-serial-number")).Trim().NullIfEmpty() ?? "Unknown";
    }

    public async Task<string> GetUuidAsync()
    {
        var v = (await ReadFileAsync("/sys/class/dmi/id/product_uuid")).Trim();
        if (v.NullIfEmpty() is not null) return v;
        return (await ExecuteCommandAsync("dmidecode", "-s system-uuid")).Trim().NullIfEmpty() ?? "Unknown";
    }

    public string GetHostname() => GetHostnameBase();
    public List<string> GetMacAddresses() => GetMacAddressesBase();

    public async Task<(string Manufacturer, string Model, int Cores, int LogicalProcessors, string Architecture, double MaxGhz, double MinGhz)> GetCpuInfoAsync()
    {
        var cpuInfo = await ReadFileAsync("/proc/cpuinfo");
        var lines = cpuInfo.Split('\n');

        var model = lines.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':').Last().Trim() ?? "Unknown";
        var vendor = lines.FirstOrDefault(l => l.StartsWith("vendor_id"))?.Split(':').Last().Trim() ?? "Unknown";
        var logical = lines.Count(l => l.StartsWith("processor"));
        var coreCount = lines.Where(l => l.StartsWith("cpu cores"))
                             .Select(l => int.TryParse(l.Split(':').Last().Trim(), out var c) ? c : 0)
                             .FirstOrDefault();

        var arch = (await ExecuteCommandAsync("uname", "-m")).Trim();

        // Frequencies
        double maxGhz = 0, minGhz = 0;
        var maxFreq = await ReadFileAsync("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
        var minFreq = await ReadFileAsync("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_min_freq");
        if (long.TryParse(maxFreq.Trim(), out var maxKhz)) maxGhz = Math.Round(maxKhz / 1_000_000.0, 2);
        if (long.TryParse(minFreq.Trim(), out var minKhz)) minGhz = Math.Round(minKhz / 1_000_000.0, 2);

        return (vendor, model, coreCount > 0 ? coreCount : logical, logical, arch, maxGhz, minGhz);
    }

    public async Task<double> GetRamTotalGbAsync()
    {
        var memInfo = await ReadFileAsync("/proc/meminfo");
        var line = memInfo.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal:")) ?? "";
        if (long.TryParse(System.Text.RegularExpressions.Regex.Match(line, @"\d+").Value, out var kb))
            return Math.Round(kb / 1024.0 / 1024.0, 2);
        return 0;
    }

    public async Task<List<RamSlot>> GetRamSlotsAsync()
    {
        var slots = new List<RamSlot>();
        var output = await ExecuteCommandAsync("dmidecode", "-t memory");
        if (string.IsNullOrWhiteSpace(output)) return slots;

        foreach (var block in output.Split("Memory Device"))
        {
            if (!block.Contains("Size:") || block.Contains("No Module")) continue;
            var lines = block.Split('\n');

            string Get(string key) => lines.FirstOrDefault(l => l.TrimStart().StartsWith(key))
                                          ?.Split(':').Last().Trim() ?? "Unknown";

            var sizeStr = Get("Size:");
            if (sizeStr == "No Module Installed" || sizeStr == "Unknown") continue;

            double sizeGb = 0;
            if (sizeStr.EndsWith("GB") && double.TryParse(sizeStr.Replace("GB", "").Trim(), out var g)) sizeGb = g;
            else if (sizeStr.EndsWith("MB") && double.TryParse(sizeStr.Replace("MB", "").Trim(), out var m)) sizeGb = Math.Round(m / 1024.0, 2);

            int.TryParse(System.Text.RegularExpressions.Regex.Match(Get("Speed:"), @"\d+").Value, out var speed);

            slots.Add(new RamSlot
            {
                Locator      = Get("Locator:"),
                SizeGb       = sizeGb,
                Type         = Get("Type:"),
                SpeedMhz     = speed,
                Manufacturer = Get("Manufacturer:"),
                PartNumber   = Get("Part Number:").Trim()
            });
        }

        return slots;
    }

    public async Task<(int Total, int Used)> GetRamSlotCountAsync()
    {
        var output = await ExecuteCommandAsync("dmidecode", "-t memory");
        var total = System.Text.RegularExpressions.Regex.Matches(output, "Memory Device").Count;
        var used  = System.Text.RegularExpressions.Regex.Matches(output, @"Size: \d+").Count;
        return (total, used);
    }

    public async Task<List<StorageDrive>> GetStorageDrivesAsync()
    {
        var drives = new List<StorageDrive>();
        var output = await ExecuteCommandAsync("lsblk", "-d -o NAME,MODEL,SIZE,ROTA --json");

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(output);
            foreach (var device in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
            {
                var name = device.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("loop")) continue;

                drives.Add(new StorageDrive
                {
                    Model  = (device.GetProperty("model").GetString() ?? "Unknown").Trim(),
                    SizeGb = ParseSizeToGb(device.GetProperty("size").GetString() ?? "0"),
                    Type   = DetermineStorageType(name, device.GetProperty("rota").GetString())
                });
            }
        }
        catch
        {
            var fallback = await ExecuteCommandAsync("lsblk", "-d -o NAME,SIZE,ROTA");
            foreach (var line in fallback.Split('\n').Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2)
                    drives.Add(new StorageDrive { Model = p[0], SizeGb = ParseSizeToGb(p[1]), Type = p.Length > 2 && p[2] == "0" ? "SSD" : "HDD" });
            }
        }

        return drives;
    }

    public async Task<List<GpuInfo>> GetGpusAsync()
    {
        // Try nvidia-smi first
        var nvOutput = await ExecuteCommandAsync("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader");
        if (!string.IsNullOrWhiteSpace(nvOutput))
        {
            return nvOutput.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(line =>
                {
                    var p = line.Split(',');
                    var mb = long.TryParse(System.Text.RegularExpressions.Regex.Match(p.Length > 1 ? p[1] : "", @"\d+").Value, out var v) ? v : 0;
                    return new GpuInfo { Model = p[0].Trim(), VramMb = mb };
                }).ToList();
        }

        // Fallback: lspci
        var gpus = new List<GpuInfo>();
        var lspci = await ExecuteCommandAsync("lspci", "-v");
        foreach (var block in lspci.Split("\n\n"))
        {
            if (!block.Contains("VGA") && !block.Contains("3D") && !block.Contains("Display")) continue;
            var nameLine = block.Split('\n').FirstOrDefault() ?? "";
            var model = nameLine.Contains(':') ? nameLine.Split(':').Last().Trim() : nameLine;

            long vram = 0;
            var memLine = block.Split('\n').FirstOrDefault(l => l.Contains("Memory") && l.Contains("prefetchable"));
            if (memLine is not null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(memLine, @"\[size=(\d+)([MG])");
                if (match.Success && long.TryParse(match.Groups[1].Value, out var size))
                    vram = match.Groups[2].Value == "G" ? size * 1024 : size;
            }
            gpus.Add(new GpuInfo { Model = model, VramMb = vram });
        }
        return gpus;
    }

    public async Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync()
    {
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            var ips = iface.GetIPProperties().UnicastAddresses
                .Select(a => a.Address.ToString())
                .ToList();

            var type = iface.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet => "Ethernet",
                NetworkInterfaceType.Wireless80211 => "WiFi",
                NetworkInterfaceType.Loopback => "Loopback",
                _ => iface.NetworkInterfaceType.ToString()
            };

            // Driver from sysfs
            var driverLink = await ExecuteCommandAsync("readlink", $"/sys/class/net/{iface.Name}/device/driver");
            var driver = driverLink.Trim().Split('/').LastOrDefault() ?? "Unknown";

            adapters.Add(new NetworkAdapterInfo
            {
                Name       = iface.Name,
                MacAddress = mac,
                IpAddresses = ips,
                Type       = type,
                SpeedMbps  = iface.Speed > 0 ? iface.Speed / 1_000_000 : 0,
                Driver     = driver.NullIfEmpty() ?? "Unknown",
                IsUp       = iface.OperationalStatus == OperationalStatus.Up
            });
        }

        return adapters;
    }

    public async Task<(string Name, string Version, DateTime LastBoot, string LastUser)> GetOsInfoAsync()
    {
        var osRelease = await ReadFileAsync("/etc/os-release");
        var name = osRelease.Split('\n').FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))?.Split('=').Last().Trim('"', '\r') ?? "Linux";
        var version = (await ExecuteCommandAsync("uname", "-r")).Trim();

        var uptimeRaw = (await ReadFileAsync("/proc/uptime")).Split(' ')[0];
        var lastBoot = double.TryParse(uptimeRaw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var u)
            ? DateTime.UtcNow.AddSeconds(-u) : DateTime.UtcNow;

        var lastUserOutput = await ExecuteCommandAsync("last", "-1 --time-format iso");
        var user = lastUserOutput.Split('\n').FirstOrDefault()?.Split(' ').FirstOrDefault() ?? "Unknown";

        return (name, version, lastBoot, user);
    }

    public async Task<(string Shell, string DisplayServer, string DesktopEnv, string Locale, int PackageCount)> GetSoftwareInfoAsync()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL")?.Split('/').Last() ?? "Unknown";
        var de = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "Unknown";
        var locale = (await ExecuteCommandAsync("locale", "")).Split('\n')
                         .FirstOrDefault(l => l.StartsWith("LANG="))?.Split('=').Last() ?? "Unknown";

        // Display server
        var displayServer = "Unknown";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) displayServer = "Wayland";
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) displayServer = "X11";

        // Package count — try multiple package managers
        int packages = 0;
        var pkgManagers = new[] { ("pacman", "-Q"), ("dpkg", "--list"), ("rpm", "-qa"), ("apk", "list --installed") };
        foreach (var (cmd, args) in pkgManagers)
        {
            var output = await ExecuteCommandAsync(cmd, args);
            if (!string.IsNullOrWhiteSpace(output))
            {
                packages = output.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
                break;
            }
        }

        return (shell, displayServer, de, locale, packages);
    }

    public async Task<(string Manufacturer, string Model, string BiosVersion, string Motherboard)> GetSystemInfoAsync()
    {
        var manufacturer = (await ReadFileAsync("/sys/class/dmi/id/sys_vendor")).Trim();
        var model        = (await ReadFileAsync("/sys/class/dmi/id/product_name")).Trim();
        var bios         = (await ReadFileAsync("/sys/class/dmi/id/bios_version")).Trim();
        var boardVendor  = (await ReadFileAsync("/sys/class/dmi/id/board_vendor")).Trim();
        var boardName    = (await ReadFileAsync("/sys/class/dmi/id/board_name")).Trim();

        return (manufacturer.NullIfEmpty() ?? "Unknown",
                model.NullIfEmpty() ?? "Unknown",
                bios.NullIfEmpty() ?? "Unknown",
                $"{boardVendor} {boardName}".Trim().NullIfEmpty() ?? "Unknown");
    }

    public async Task<List<DisplayInfo>> GetDisplaysAsync()
    {
        var displays = new List<DisplayInfo>();
        var output = await ExecuteCommandAsync("xrandr", "--query");

        foreach (var line in output.Split('\n').Where(l => l.Contains(" connected")))
        {
            var primary = line.Contains("primary");
            var resMatch = System.Text.RegularExpressions.Regex.Match(line, @"\d{3,4}x\d{3,4}");
            displays.Add(new DisplayInfo { Resolution = resMatch.Success ? resMatch.Value : "Unknown", IsPrimary = primary });
        }

        return displays;
    }

    public async Task<SecurityInfo> GetSecurityInfoAsync()
    {
        var security = new SecurityInfo();

        // Secure Boot
        var sbOutput = await ReadFileAsync("/sys/firmware/efi/efivars/SecureBoot-8be4df61-93ca-11d2-aa0d-00e098032b8c");
        security.SecureBootEnabled = sbOutput.Length >= 5 && sbOutput[4] == '\x01';

        // TPM
        var tpmDir = Directory.Exists("/sys/class/tpm") ? Directory.GetDirectories("/sys/class/tpm") : [];
        if (tpmDir.Length > 0)
        {
            var tpmVersion = await ReadFileAsync(Path.Combine(tpmDir[0], "tpm_version_major"));
            security.TpmVersion = tpmVersion.Trim().NullIfEmpty() ?? "Unknown";
        }
        else security.TpmVersion = "None";

        // LUKS (disk encryption)
        var luksOutput = await ExecuteCommandAsync("lsblk", "-o TYPE");
        security.DiskEncryptionEnabled = luksOutput.Contains("crypt");

        // Firewall
        var ufwOutput = await ExecuteCommandAsync("ufw", "status");
        var nftOutput = await ExecuteCommandAsync("nft", "list ruleset");
        var iptOutput = await ExecuteCommandAsync("iptables", "-L -n");
        security.FirewallActive = ufwOutput.Contains("active") || !string.IsNullOrWhiteSpace(nftOutput) || iptOutput.Contains("ACCEPT");

        // AppArmor
        var aaOutput = await ExecuteCommandAsync("aa-status", "--enabled");
        if (aaOutput.Contains("0")) security.AppArmorStatus = "enabled";
        else
        {
            var aaProfile = await ReadFileAsync("/sys/kernel/security/apparmor/profiles");
            security.AppArmorStatus = string.IsNullOrWhiteSpace(aaProfile) ? "disabled" : "enforcing";
        }

        // Failed logins
        var failedOutput = await ExecuteCommandAsync("journalctl", "_COMM=sshd --since '24h ago' -q --no-pager");
        security.FailedLoginAttempts = failedOutput.Split('\n').Count(l => l.Contains("Failed password") || l.Contains("Invalid user"));

        // Open ports
        var ssOutput = await ExecuteCommandAsync("ss", "-tlnp");
        security.OpenPorts = ssOutput.Split('\n')
            .Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @":(\d+)\s").Groups[1].Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        // SSH keys
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        security.SshKeysPresent = Directory.Exists(sshDir) && Directory.GetFiles(sshDir, "*.pub").Length > 0;

        // Pending security updates (pacman)
        var updatesOutput = await ExecuteCommandAsync("checkupdates", "");
        security.PendingSecurityUpdates = string.IsNullOrWhiteSpace(updatesOutput) ? 0
            : updatesOutput.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));

        return security;
    }

    public async Task<VirtualizationInfo> GetVirtualizationInfoAsync()
    {
        var info = new VirtualizationInfo();

        // Check for container
        var cgroup = await ReadFileAsync("/proc/1/cgroup");
        if (cgroup.Contains("docker") || cgroup.Contains("containerd") || File.Exists("/.dockerenv"))
        {
            info.IsContainer = true;
            info.IsVirtualMachine = false;
            info.HypervisorType = "Docker";
            return info;
        }

        // systemd-detect-virt
        var virt = (await ExecuteCommandAsync("systemd-detect-virt", "")).Trim().ToLower();
        if (virt is "none" or "") return info;

        info.IsVirtualMachine = true;
        info.HypervisorType = virt switch
        {
            "kvm"       => "KVM",
            "vmware"    => "VMware",
            "oracle"    => "VirtualBox",
            "microsoft" => "Hyper-V",
            "xen"       => "Xen",
            "hyperv"    => "Hyper-V",
            _           => virt
        };

        return info;
    }

    // Metrics

    private double[] _lastCoreIdle  = [];
    private double[] _lastCoreTotal = [];

    public double GetCpuLoadPercent()
    {
        var stat = File.ReadAllText("/proc/stat").Split('\n').FirstOrDefault(l => l.StartsWith("cpu ")) ?? "";
        var nums = stat.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                       .Select(s => double.TryParse(s, out var n) ? n : 0).ToArray();
        if (nums.Length < 4) return 0;

        var idle = nums[3]; var total = nums.Sum();
        var di = idle - _lastGlobalIdle; var dt = total - _lastGlobalTotal;
        _lastGlobalIdle = idle; _lastGlobalTotal = total;
        return dt > 0 ? Math.Round((1.0 - di / dt) * 100.0, 2) : 0;
    }

    private double _lastGlobalIdle, _lastGlobalTotal;

    public List<double> GetPerCoreCpuPercent()
    {
        var lines = File.ReadAllText("/proc/stat").Split('\n')
                        .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^cpu\d+"))
                        .ToArray();

        if (_lastCoreIdle.Length != lines.Length)
        {
            _lastCoreIdle  = new double[lines.Length];
            _lastCoreTotal = new double[lines.Length];
        }

        var result = new List<double>();
        for (int i = 0; i < lines.Length; i++)
        {
            var nums = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                               .Select(s => double.TryParse(s, out var n) ? n : 0).ToArray();
            if (nums.Length < 4) { result.Add(0); continue; }

            var idle = nums[3]; var total = nums.Sum();
            var di = idle - _lastCoreIdle[i]; var dt = total - _lastCoreTotal[i];
            _lastCoreIdle[i] = idle; _lastCoreTotal[i] = total;
            result.Add(dt > 0 ? Math.Round((1.0 - di / dt) * 100.0, 1) : 0);
        }
        return result;
    }

    public double GetCpuTemperature()
    {
        try
        {
            var zones = Directory.GetFiles("/sys/class/thermal/", "temp", SearchOption.AllDirectories);
            if (zones.Length == 0) return 0;
            if (long.TryParse(File.ReadAllText(zones[0]).Trim(), out var md)) return Math.Round(md / 1000.0, 1);
        }
        catch { }
        return 0;
    }

    public (double m1, double m5, double m15) GetLoadAverage()
    {
        try
        {
            var parts = File.ReadAllText("/proc/loadavg").Split(' ');
            var m1  = double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 0;
            var m5  = double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0;
            var m15 = double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 0;
            return (m1, m5, m15);
        }
        catch { return (0, 0, 0); }
    }

    public double GetRamUsedGb()
    {
        var lines = File.ReadAllText("/proc/meminfo").Split('\n');
        long Get(string key) => lines.Where(l => l.StartsWith(key))
            .Select(l => long.TryParse(System.Text.RegularExpressions.Regex.Match(l, @"\d+").Value, out var v) ? v : 0)
            .FirstOrDefault();
        return Math.Round((Get("MemTotal:") - Get("MemAvailable:")) / 1024.0 / 1024.0, 2);
    }

    public double GetSwapUsedGb()
    {
        try
        {
            var lines = File.ReadAllText("/proc/meminfo").Split('\n');
            long Get(string key) => lines.Where(l => l.StartsWith(key))
                .Select(l => long.TryParse(System.Text.RegularExpressions.Regex.Match(l, @"\d+").Value, out var v) ? v : 0)
                .FirstOrDefault();
            return Math.Round((Get("SwapTotal:") - Get("SwapFree:")) / 1024.0 / 1024.0, 2);
        }
        catch { return 0; }
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
            var lines = File.ReadAllText("/proc/diskstats").Split('\n');
            long totalRead = 0, totalWrite = 0;

            foreach (var line in lines)
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 14) continue;
                var name = p[2];
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^(sd[a-z]|nvme\d+n\d+|hd[a-z])$")) continue;

                if (long.TryParse(p[5], out var r)) totalRead += r;
                if (long.TryParse(p[9], out var w)) totalWrite += w;
            }

            // sectors = 512 bytes
            var readMbps  = Math.Round((totalRead  - _lastDiskRead)  * 512.0 / 1024 / 1024, 2);
            var writeMbps = Math.Round((totalWrite - _lastDiskWrite) * 512.0 / 1024 / 1024, 2);
            _lastDiskRead = totalRead; _lastDiskWrite = totalWrite;
            return (Math.Max(0, readMbps), Math.Max(0, writeMbps));
        }
        catch { return (0, 0); }
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
        try
        {
            var output = ExecuteCommandAsync("nvidia-smi",
                "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader").Result;
            if (string.IsNullOrWhiteSpace(output)) return (0, 0);
            var parts = output.Split(',');
            var usage = double.TryParse(System.Text.RegularExpressions.Regex.Match(parts[0], @"\d+").Value, out var u) ? u : 0;
            var temp  = double.TryParse(parts.Length > 1 ? parts[1].Trim() : "", out var t) ? t : 0;
            return (usage, temp);
        }
        catch { return (0, 0); }
    }

    public (double HealthPercent, int CycleCount) GetBatteryInfo()
    {
        try
        {
            var batteries = Directory.GetDirectories("/sys/class/power_supply/")
                .Where(d => File.Exists(Path.Combine(d, "type"))
                         && File.ReadAllText(Path.Combine(d, "type")).Trim() == "Battery")
                .ToList();
            if (!batteries.Any()) return (0, 0);
            var bat = batteries[0];
            double.TryParse(File.ReadAllText(Path.Combine(bat, "capacity")).Trim(), out var cap);
            int.TryParse(File.ReadAllText(Path.Combine(bat, "cycle_count")).Trim(), out var cycles);
            return (cap, cycles);
        }
        catch { return (0, 0); }
    }

    public List<ProcessInfo> GetTopCpuProcesses(int count = 5) =>
        Process.GetProcesses()
            .OrderByDescending(p => { try { return p.TotalProcessorTime.TotalMilliseconds; } catch { return 0.0; } })
            .Take(count)
            .Select(p => { try { return new ProcessInfo { Name = p.ProcessName, RamMb = Math.Round((double)p.WorkingSet64 / 1024 / 1024, 1) }; } catch { return null!; } })
            .Where(p => p is not null)
            .ToList();

    public List<ProcessInfo> GetTopRamProcesses(int count = 5) =>
        Process.GetProcesses()
            .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
            .Take(count)
            .Select(p => { try { return new ProcessInfo { Name = p.ProcessName, RamMb = Math.Round((double)p.WorkingSet64 / 1024 / 1024, 1) }; } catch { return null!; } })
            .Where(p => p is not null)
            .ToList();

    //Helpers

    private static string DetermineStorageType(string name, string? rota)
    {
        if (name.StartsWith("nvme")) return "NVMe";
        return rota == "0" ? "SSD" : rota == "1" ? "HDD" : "Unknown";
    }

    private static double ParseSizeToGb(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.EndsWith("T") && double.TryParse(s[..^1], out var t)) return t * 1024;
        if (s.EndsWith("G") && double.TryParse(s[..^1], out var g)) return g;
        if (s.EndsWith("M") && double.TryParse(s[..^1], out var m)) return Math.Round(m / 1024.0, 2);
        return 0;
    }
}