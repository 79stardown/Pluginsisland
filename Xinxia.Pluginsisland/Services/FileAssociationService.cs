using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;

namespace Xinxia.Pluginsisland.Services;

/// <summary>
/// .cipx 文件关联服务：注册/注销/状态查询均委托给自带的 Pluginsisland.Helper.exe；
/// 状态查询经 advapi32 读 HKCU（零 NuGet 依赖，避免与宿主依赖副本冲突）。
/// </summary>
public sealed class FileAssociationService
{
    public const string ProgId = "ClassIsland.PluginPackage";

    public string HelperPath { get; }
    private readonly string _ciRoot;

    public FileAssociationService()
    {
        var folder = IPluginService.LoadedPlugins
            .FirstOrDefault(p => p.Manifest.Id == PluginsislandRuntime.PluginId)?.PluginFolderPath
            ?? Path.GetDirectoryName(typeof(FileAssociationService).Assembly.Location);
        HelperPath = Path.Combine(folder ?? "", "Pluginsisland.Helper.exe");
        _ciRoot = Directory.GetParent(CommonDirectories.AppRootFolderPath)?.FullName ?? "";
    }

    public void Register()
    {
        if (!File.Exists(HelperPath))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = HelperPath,
            Arguments = $"--register --ci-root \"{_ciRoot}\"",
            UseShellExecute = false,
        });
    }

    public void Unregister()
    {
        if (!File.Exists(HelperPath))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = HelperPath,
            Arguments = "--unregister",
            UseShellExecute = false,
        });
    }

    /// <summary>卸载本插件前先注销关联，防残留指向已删除的 exe。</summary>
    public void UnregisterIfSelf()
    {
        if (IsRegistered())
            Unregister();
    }

    public bool IsRegistered() =>
        File.Exists(HelperPath) && RegistryProbe.GetDefaultValue(@"Software\Classes\.cipx") == ProgId;

    /// <summary>极简注册表读取（仅查询默认值），advapi32 P/Invoke。</summary>
    private static class RegistryProbe
    {
        private static readonly IntPtr Hkcu = new(0x80000001);
        private const int KeyRead = 0x20019;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueEx(IntPtr hKey, string? lpValueName, IntPtr lpReserved, out int lpType, byte[]? lpData, ref int lpcbData);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);

        public static string? GetDefaultValue(string subKey)
        {
            if (RegOpenKeyEx(Hkcu, subKey, 0, KeyRead, out var key) != 0)
                return null;
            try
            {
                var size = 0;
                if (RegQueryValueEx(key, null, IntPtr.Zero, out _, null, ref size) != 0)
                    return null;
                var data = new byte[size];
                if (RegQueryValueEx(key, null, IntPtr.Zero, out _, data, ref size) != 0)
                    return null;
                var len = size >= 2 ? size - 2 : 0;
                return Encoding.Unicode.GetString(data, 0, len);
            }
            finally
            {
                RegCloseKey(key);
            }
        }
    }
}
