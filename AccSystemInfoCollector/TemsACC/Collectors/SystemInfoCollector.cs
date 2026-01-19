using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using TemsACC.Models;

namespace TemsACC.Collectors;

public class SystemInfoCollector : ISystemInfoCollector
{
    private readonly ILogger<SystemInfoCollector> _logger;
    private long _lastNetworkBytesSent;
    private long _lastNetworkBytesReceived;
    private DateTime _lastNetworkCheck = DateTime.UtcNow;

    public SystemInfoCollector(ILogger<SystemInfoCollector> logger)
    {
        _logger = logger;
    }

    public async Task<SystemProperties> CollectSystemPropertiesAsync()
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Collecting system properties...");
            
            var properties = new SystemProperties
            {
                SerialNumber = GetSerialNumber(),
                MacAddresses = GetMacAddresses(),
                Uuid = GetUuid(),
                Hostname = Environment.MachineName,
                Cpu = GetCpuInfo(),
                Ram = GetRamInfo(),
                Storage = GetStorageInfo(),
                Gpu = GetGpuInfo(),
                Os = GetOsInfo(),
                CollectedAt = DateTime.UtcNow
            };

            // Log all collected properties in detail
            _logger.LogInformation("=== System Properties Collected ===");
            _logger.LogInformation("Serial Number: {Serial}", properties.SerialNumber);
            _logger.LogInformation("MAC Addresses: {Mac}", string.Join(", ", properties.MacAddresses));
            _logger.LogInformation("UUID: {Uuid}", properties.Uuid);
            _logger.LogInformation("Hostname: {Host}", properties.Hostname);
            
            _logger.LogInformation("CPU:");
            _logger.LogInformation("  - Manufacturer: {Manufacturer}", properties.Cpu.Manufacturer);
            _logger.LogInformation("  - Model: {Model}", properties.Cpu.Model);
            _logger.LogInformation("  - Cores: {Cores}", properties.Cpu.Cores);
            _logger.LogInformation("  - Logical Processors: {Logical}", properties.Cpu.LogicalProcessors);
            _logger.LogInformation("  - Architecture: {Arch}", properties.Cpu.Architecture);
            
            _logger.LogInformation("RAM:");
            _logger.LogInformation("  - Total Capacity: {Ram} GB", properties.Ram.TotalCapacityGb);
            
            _logger.LogInformation("Storage ({Count} drives):", properties.Storage.Count);
            foreach (var storage in properties.Storage)
            {
                _logger.LogInformation("  - {Model}: {Size} GB ({Type})", 
                    storage.Model, storage.SizeGb, storage.Type);
            }
            
            _logger.LogInformation("GPU ({Count} devices):", properties.Gpu.Count);
            foreach (var gpu in properties.Gpu)
            {
                _logger.LogInformation("  - {Model} (VRAM: {Vram} MB)", 
                    gpu.Model, gpu.VramMb);
            }
            
            _logger.LogInformation("OS:");
            _logger.LogInformation("  - Name: {Name}", properties.Os.Name);
            _logger.LogInformation("  - Version: {Version}", properties.Os.Version);
            _logger.LogInformation("  - Last Boot Time: {Boot}", properties.Os.LastBootTime);
            _logger.LogInformation("  - Last Logged In User: {User}", properties.Os.LastLoggedInUser);
            _logger.LogInformation("===================================");

            return properties;
        });
    }

    public async Task<MetricsSample> CollectMetricsAsync()
    {
        return await Task.Run(() =>
        {
            var metrics = new MetricsSample
            {
                Timestamp = DateTime.UtcNow,
                CpuLoadPercent = GetCpuUsage(),
                RamUsedGb = GetRamUsage(),
                DiskFreeGb = GetDiskFreeSpace(),
                NetworkBytesSent = GetNetworkBytesSent(),
                NetworkBytesReceived = GetNetworkBytesReceived(),
                BatteryHealthPercent = GetBatteryHealth(),
                BatteryCycleCount = GetBatteryCycleCount()
            };

            return metrics;
        });
    }

    #region Properties Collection

    private string GetSerialNumber()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "bios get serialnumber");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length > 1 ? lines[1].Trim() : "Unknown";
            }
            else if (OperatingSystem.IsLinux())
            {
                var result = ReadFile("/sys/class/dmi/id/product_serial");
                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogWarning("Serial number is 'Unknown'. This may require root/sudo access on Linux. Try running with elevated privileges.");
                    return "Unknown";
                }
                return result.Trim();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("system_profiler", "SPHardwareDataType | grep 'Serial Number'");
                return output.Contains(':') ? output.Split(':').Last().Trim() : "Unknown";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get serial number");
        }
        return "Unknown";
    }

    private List<string> GetMacAddresses()
    {
        var macAddresses = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var mac = nic.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                    {
                        macAddresses.Add(FormatMacAddress(mac));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get MAC addresses");
        }
        return macAddresses;
    }

    private string FormatMacAddress(string mac)
    {
        return string.Join(":", Enumerable.Range(0, mac.Length / 2)
            .Select(i => mac.Substring(i * 2, 2)));
    }

    private string GetUuid()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "csproduct get uuid");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length > 1 ? lines[1].Trim() : "Unknown";
            }
            else if (OperatingSystem.IsLinux())
            {
                var result = ReadFile("/sys/class/dmi/id/product_uuid");
                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogWarning("UUID is 'Unknown'. This may require root/sudo access on Linux. Try running with elevated privileges.");
                    return "Unknown";
                }
                return result.Trim();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("system_profiler", "SPHardwareDataType | grep 'Hardware UUID'");
                return output.Contains(':') ? output.Split(':').Last().Trim() : "Unknown";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get UUID");
        }
        return "Unknown";
    }

    private CpuInfo GetCpuInfo()
    {
        var cpu = new CpuInfo();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "cpu get name,manufacturer,numberofcores,numberoflogicalprocessors /format:list");
                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("Manufacturer="))
                        cpu.Manufacturer = line.Split('=')[1].Trim();
                    else if (line.StartsWith("Name="))
                        cpu.Model = line.Split('=')[1].Trim();
                    else if (line.StartsWith("NumberOfCores="))
                    {
                        if (int.TryParse(line.Split('=')[1].Trim(), out var cores))
                            cpu.Cores = cores;
                    }
                    else if (line.StartsWith("NumberOfLogicalProcessors="))
                    {
                        if (int.TryParse(line.Split('=')[1].Trim(), out var logical))
                            cpu.LogicalProcessors = logical;
                    }
                }
                cpu.Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            }
            else if (OperatingSystem.IsLinux())
            {
                var cpuinfo = ReadFile("/proc/cpuinfo");
                foreach (var line in cpuinfo.Split('\n'))
                {
                    if (line.StartsWith("model name"))
                        cpu.Model = line.Split(':')[1].Trim();
                    else if (line.StartsWith("vendor_id"))
                        cpu.Manufacturer = line.Split(':')[1].Trim();
                    else if (line.StartsWith("cpu cores"))
                    {
                        if (int.TryParse(line.Split(':')[1].Trim(), out var cores))
                            cpu.Cores = cores;
                    }
                }
                
                // Count all CPU directories (cpu0, cpu1, cpu2, ..., cpu11, etc.)
                var cpuDirs = Directory.GetDirectories("/sys/devices/system/cpu/")
                    .Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^cpu\d+$"))
                    .ToArray();
                cpu.LogicalProcessors = cpuDirs.Length;
                cpu.Architecture = ExecuteCommand("uname", "-m").Trim();
            }
            else if (OperatingSystem.IsMacOS())
            {
                cpu.Model = ExecuteCommand("sysctl", "-n machdep.cpu.brand_string").Trim();
                
                if (int.TryParse(ExecuteCommand("sysctl", "-n hw.physicalcpu").Trim(), out var cores))
                    cpu.Cores = cores;
                    
                if (int.TryParse(ExecuteCommand("sysctl", "-n hw.logicalcpu").Trim(), out var logical))
                    cpu.LogicalProcessors = logical;
                    
                cpu.Architecture = ExecuteCommand("uname", "-m").Trim();
                cpu.Manufacturer = "Apple";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get CPU info");
        }
        return cpu;
    }

    private RamInfo GetRamInfo()
    {
        var ram = new RamInfo();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "computersystem get totalphysicalmemory");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 1 && long.TryParse(lines[1].Trim(), out var bytes))
                {
                    ram.TotalCapacityGb = Math.Round(bytes / (1024.0 * 1024.0 * 1024.0), 2);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                var meminfo = ReadFile("/proc/meminfo");
                var match = System.Text.RegularExpressions.Regex.Match(meminfo, @"MemTotal:\s+(\d+)\s+kB");
                if (match.Success)
                {
                    ram.TotalCapacityGb = Math.Round(long.Parse(match.Groups[1].Value) / (1024.0 * 1024.0), 2);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("sysctl", "-n hw.memsize").Trim();
                if (long.TryParse(output, out var bytes))
                {
                    ram.TotalCapacityGb = Math.Round(bytes / (1024.0 * 1024.0 * 1024.0), 2);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get RAM info");
        }
        return ram;
    }

    private List<StorageInfo> GetStorageInfo()
    {
        var storageList = new List<StorageInfo>();
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    storageList.Add(new StorageInfo
                    {
                        Model = drive.Name,
                        SizeGb = Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 2),
                        Type = "Fixed"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get storage info");
        }
        return storageList;
    }

    private List<GpuInfo> GetGpuInfo()
    {
        var gpuList = new List<GpuInfo>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "path win32_videocontroller get name");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1);
                foreach (var line in lines)
                {
                    gpuList.Add(new GpuInfo { Model = line.Trim(), VramMb = 0 });
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                var output = ExecuteCommand("lspci", "| grep -i vga");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var parts = output.Split(':');
                    gpuList.Add(new GpuInfo { Model = parts.Length > 2 ? parts[2].Trim() : output.Trim(), VramMb = 0 });
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("system_profiler", "SPDisplaysDataType | grep 'Chipset Model'");
                if (!string.IsNullOrWhiteSpace(output) && output.Contains(':'))
                {
                    gpuList.Add(new GpuInfo { Model = output.Split(':').Last().Trim(), VramMb = 0 });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get GPU info");
        }
        return gpuList;
    }

    private OsInfo GetOsInfo()
    {
        var os = new OsInfo();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "os get caption");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                os.Name = lines.Length > 1 ? lines[1].Trim() : "Windows";
                os.Version = Environment.OSVersion.Version.ToString();
                os.LastBootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
                os.LastLoggedInUser = Environment.UserName;
            }
            else if (OperatingSystem.IsLinux())
            {
                var osRelease = ReadFile("/etc/os-release");
                var match = System.Text.RegularExpressions.Regex.Match(osRelease, @"PRETTY_NAME=""(.+?)""");
                os.Name = match.Success ? match.Groups[1].Value : "Linux";
                os.Version = ExecuteCommand("uname", "-r").Trim();

                var uptimeStr = ExecuteCommand("uptime", "-s").Trim();
                if (DateTime.TryParse(uptimeStr, out var bootTime))
                {
                    os.LastBootTime = bootTime;
                }

                os.LastLoggedInUser = ExecuteCommand("whoami", "").Trim();
            }
            else if (OperatingSystem.IsMacOS())
            {
                os.Name = "macOS";
                os.Version = ExecuteCommand("sw_vers", "-productVersion").Trim();
                os.LastBootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
                os.LastLoggedInUser = Environment.UserName;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get OS info");
        }
        return os;
    }

    #endregion

    #region Metrics Collection

    private double GetCpuUsage()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var stat1 = ReadFile("/proc/stat").Split('\n')[0];
                var values1 = stat1.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
                var idle1 = values1[3];
                var total1 = values1.Sum();

                Thread.Sleep(100);

                var stat2 = ReadFile("/proc/stat").Split('\n')[0];
                var values2 = stat2.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
                var idle2 = values2[3];
                var total2 = values2.Sum();

                var idleDelta = idle2 - idle1;
                var totalDelta = total2 - total1;

                return Math.Round((1.0 - (double)idleDelta / totalDelta) * 100, 2);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("top", "-l 1 | grep 'CPU usage'");
                // Simplified parsing
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get CPU usage");
        }
        return 0;
    }

    private double GetRamUsage()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var meminfo = ReadFile("/proc/meminfo");
                var totalMatch = System.Text.RegularExpressions.Regex.Match(meminfo, @"MemTotal:\s+(\d+)\s+kB");
                var availMatch = System.Text.RegularExpressions.Regex.Match(meminfo, @"MemAvailable:\s+(\d+)\s+kB");

                if (totalMatch.Success && availMatch.Success)
                {
                    var totalKb = long.Parse(totalMatch.Groups[1].Value);
                    var availKb = long.Parse(availMatch.Groups[1].Value);
                    return Math.Round((totalKb - availKb) / (1024.0 * 1024.0), 2);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                // macOS memory calculation
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get RAM usage");
        }
        return 0;
    }

    private double GetDiskFreeSpace()
    {
        try
        {
            var systemDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
            if (systemDrive != null)
            {
                return Math.Round(systemDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get disk free space");
        }
        return 0;
    }

    private long GetNetworkBytesSent()
    {
        try
        {
            long totalSent = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    totalSent += nic.GetIPv4Statistics().BytesSent;
                }
            }
            
            var delta = totalSent - _lastNetworkBytesSent;
            _lastNetworkBytesSent = totalSent;
            return delta;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get network bytes sent");
        }
        return 0;
    }

    private long GetNetworkBytesReceived()
    {
        try
        {
            long totalReceived = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    totalReceived += nic.GetIPv4Statistics().BytesReceived;
                }
            }
            
            var delta = totalReceived - _lastNetworkBytesReceived;
            _lastNetworkBytesReceived = totalReceived;
            return delta;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get network bytes received");
        }
        return 0;
    }

    private int GetBatteryHealth()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = ExecuteCommand("wmic", "path Win32_Battery get EstimatedChargeRemaining");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 1 && int.TryParse(lines[1].Trim(), out var charge))
                {
                    return charge;
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                var capacity = ReadFile("/sys/class/power_supply/BAT0/capacity");
                if (int.TryParse(capacity.Trim(), out var charge))
                {
                    return charge;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("pmset", "-g batt | grep -Eo '[0-9]+%'");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return int.Parse(output.Replace("%", "").Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get battery health");
        }
        return 0;
    }

    private int GetBatteryCycleCount()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var cycles = ReadFile("/sys/class/power_supply/BAT0/cycle_count");
                if (int.TryParse(cycles.Trim(), out var count))
                {
                    return count;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var output = ExecuteCommand("system_profiler", "SPPowerDataType | grep 'Cycle Count'");
                if (!string.IsNullOrWhiteSpace(output) && output.Contains(':'))
                {
                    var cycleStr = output.Split(':').Last().Trim();
                    if (int.TryParse(cycleStr, out var count))
                    {
                        return count;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get battery cycle count");
        }
        return 0;
    }

    #endregion

    #region Helper Methods

    private string ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ExecuteCommand(string command, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows() ? $"/c {command} {arguments}" : $"-c \"{command} {arguments}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }
        catch
        {
            // Ignore
        }
        return string.Empty;
    }

    #endregion
}