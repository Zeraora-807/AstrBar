using Microsoft.Win32;

namespace AstrBar.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AstrBar";

    public void Apply(bool enabled)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                           ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                                 ?? throw new InvalidOperationException("无法获取 AstrBar 可执行文件路径。");
            runKey.SetValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
