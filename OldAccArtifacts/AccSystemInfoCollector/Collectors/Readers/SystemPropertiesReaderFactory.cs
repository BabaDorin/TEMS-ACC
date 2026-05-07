namespace TEMS.ACC.Collectors.Readers;

public static class SystemPropertiesReaderFactory
{
    public static ISystemPropertiesReader Create() =>
        OperatingSystem.IsWindows() ? new WindowsPropertiesReader() :
        OperatingSystem.IsMacOS()   ? new MacOsPropertiesReader()   :
        new LinuxPropertiesReader();
}