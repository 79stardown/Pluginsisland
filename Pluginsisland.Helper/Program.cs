using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Pluginsisland.Helper;

/// <summary>
/// Pluginsisland 安装助手：注册/注销 .cipx 文件关联，处理双击 .cipx 安装。
/// 零 NuGet 依赖——注册表经 advapi32 P/Invoke，提示经 user32 MessageBox。
/// </summary>
internal static class Program
{
    private const string ProgId = "ClassIsland.PluginPackage";
    private const string AuthorKey = @"Software\xinxia.pluginsisland";
    private const string MutexName = @"Global\ClassIsland.Lock";

    private static readonly IntPtr Hkcu = new(0x80000001);
    private const int RegSz = 1;
    private const int KeyRead = 0x20019;
    private const int KeyWrite = 0x20006;
    private const int ErrorSuccess = 0;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Msg("请从 ClassIsland 的「插件管理」设置页注册 .cipx 文件关联，然后双击 .cipx 文件即可快速安装插件。", "Pluginsisland 安装助手");
                return 2;
            }

            return args[0] switch
            {
                "--register" => Register(GetArgValue(args, "--ci-root")),
                "--unregister" => Unregister(),
                "--install" => Install(GetArgValue(args, "--install")),
                _ => Install(args[0]),
            };
        }
        catch (Exception ex)
        {
            Msg(ex.Message, "Pluginsisland 安装助手");
            return 1;
        }
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    // ---------- 注册 / 注销 ----------

    private static int Register(string? ciRoot)
    {
        if (string.IsNullOrWhiteSpace(ciRoot))
        {
            Msg("未提供 ClassIsland 根目录（--ci-root）。请从「插件管理」设置页操作。", "Pluginsisland 安装助手");
            return 1;
        }
        if (!File.Exists(Path.Combine(ciRoot, "ClassIsland.exe")))
        {
            Msg($"未在「{ciRoot}」中找到 ClassIsland.exe，注册中止。", "Pluginsisland 安装助手");
            return 1;
        }

        var existing = GetDefaultValue(@"Software\Classes\.cipx");
        if (!string.IsNullOrEmpty(existing) && existing != ProgId)
        {
            if (MsgYesNo($"检测到 .cipx 已被其他程序关联（当前值：{existing}）。是否覆盖为 Pluginsisland 的安装助手？", "Pluginsisland 安装助手") != 6)
                return 3;
        }

        var helperDir = AppContext.BaseDirectory;
        var helperExe = Path.Combine(helperDir, "Pluginsisland.Helper.exe");
        // 优先用随包分发的 App.ico（可换图标而不必重编译），缺失时回落到 exe 内嵌图标
        var icoPath = Path.Combine(helperDir, "App.ico");
        SetDefaultValue(@"Software\Classes\.cipx", ProgId);
        SetDefaultValue(@"Software\Classes\" + ProgId, "ClassIsland 插件包");
        SetDefaultValue(@"Software\Classes\" + ProgId + @"\DefaultIcon",
            File.Exists(icoPath) ? $"\"{icoPath}\",0" : $"\"{helperExe}\",0");
        SetDefaultValue(@"Software\Classes\" + ProgId + @"\shell\open\command", $"\"{helperExe}\" \"%1\"");
        SetValue(AuthorKey, "CiRoot", ciRoot);
        Msg("已注册 .cipx 文件关联。现在双击 .cipx 文件即可快速安装插件。", "Pluginsisland 安装助手");
        return 0;
    }

    private static int Unregister()
    {
        if (GetDefaultValue(@"Software\Classes\.cipx") == ProgId)
        {
            DeleteTree(@"Software\Classes\.cipx");
            DeleteTree(@"Software\Classes\" + ProgId);
            DeleteValue(AuthorKey, "CiRoot");
        }
        else
        {
            Msg(".cipx 文件关联不是由 Pluginsisland 注册的，无需注销。", "Pluginsisland 安装助手");
            return 3;
        }
        Msg("已注销 .cipx 文件关联。", "Pluginsisland 安装助手");
        return 0;
    }

    // ---------- 安装 ----------

    private static int Install(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Msg("未找到要安装的 .cipx 插件包文件。", "Pluginsisland 安装助手");
            return 1;
        }
        if (!filePath.EndsWith(".cipx", StringComparison.OrdinalIgnoreCase))
        {
            Msg("仅支持 .cipx 插件包文件。", "Pluginsisland 安装助手");
            return 1;
        }

        var (id, name, version) = ReadManifest(filePath);
        var package = string.IsNullOrEmpty(name) ? Path.GetFileName(filePath) : name;

        // 暂存：复制到 CI 缓存目录，重启（或下次启动）时由 ClassIsland 自动安装。
        // 无论后续 IPC 转发是否成功，插件包都不会丢失。
        var helperDir = AppContext.BaseDirectory;
        var cacheDir = Path.GetFullPath(Path.Combine(helperDir, "..", "..", "Cache", "PluginPackages"));
        Directory.CreateDirectory(cacheDir);
        File.Copy(filePath, Path.Combine(cacheDir, Path.GetFileName(filePath)), true);

        var ciRoot = ResolveCiRoot();
        var ciExe = ciRoot is null ? null : Path.Combine(ciRoot, "ClassIsland.exe");
        if (ciExe is null || !File.Exists(ciExe))
        {
            Msg("未找到 ClassIsland 程序。插件包已暂存，下次启动 ClassIsland 时将自动安装。", "Pluginsisland 安装助手");
            return 1;
        }

        if (IsClassIslandRunning())
        {
            // 分支 A：CI 运行中。启动第二实例并带 --uri，第二实例经 IPC 转发给运行实例，
            // 由插件弹出确认对话框后重启安装。
            var uri = "classisland://plugins/xinxia.pluginsisland/install?package=" + Uri.EscapeDataString(Path.GetFileName(filePath));
            Process.Start(new ProcessStartInfo
            {
                FileName = ciExe,
                WorkingDirectory = ciRoot!,
                UseShellExecute = true,
                Arguments = $"--uri \"{uri}\"",
            });
        }
        else
        {
            // 分支 B：CI 未运行。直接启动，CI 启动早期会处理缓存中的插件包。
            Process.Start(new ProcessStartInfo
            {
                FileName = ciExe,
                WorkingDirectory = ciRoot!,
                UseShellExecute = true,
            });
            Msg($"已暂存插件「{package}」{(string.IsNullOrEmpty(version) ? "" : " " + version)}（{id}），ClassIsland 正在启动并安装。", "Pluginsisland 安装助手");
        }
        return 0;
    }

    private static (string? Id, string? Name, string? Version) ReadManifest(string cipxPath)
    {
        string? id = null, name = null, version = null;
        using var zip = ZipFile.OpenRead(cipxPath);
        var entry = zip.GetEntry("manifest.yml");
        if (entry is null)
            return (null, null, null);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var t = line.Trim();
            if (t.StartsWith("id:", StringComparison.OrdinalIgnoreCase)) id = TrimYaml(t["id:".Length..]);
            else if (t.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) name = TrimYaml(t["name:".Length..]);
            else if (t.StartsWith("version:", StringComparison.OrdinalIgnoreCase)) version = TrimYaml(t["version:".Length..]);
        }
        return (id, name, version);

        static string TrimYaml(string s) => s.Trim().Trim('"', '\'');
    }

    private static bool IsClassIslandRunning()
    {
        try
        {
            using (var mutex = new Mutex(true, MutexName, out var createdNew))
            {
                if (!createdNew)
                    return true;
            }
            // 若本进程创建了互斥体，using 块结束时已释放，不会妨碍 CI 启动
        }
        catch (UnauthorizedAccessException)
        {
            // CI 可能以更高权限运行，本进程无法探测互斥体，视为运行中
            return true;
        }
        return Process.GetProcessesByName("ClassIsland").Length > 0;
    }

    private static string? ResolveCiRoot()
    {
        // 1. 注册表（--register 时写入的绝对路径）
        var saved = GetValue(AuthorKey, "CiRoot");
        if (!string.IsNullOrEmpty(saved) && File.Exists(Path.Combine(saved, "ClassIsland.exe")))
            return saved;

        // 2. 便携布局推导：helper 位于 <根>\data\Plugins\xinxia.pluginsisland\
        var dir = AppContext.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        if (File.Exists(Path.Combine(candidate, "ClassIsland.exe")))
            return candidate;

        // 3. 逐级上溯查找
        for (var d = Directory.GetParent(dir); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ClassIsland.exe")))
                return d.FullName;
        }
        return null;
    }

    // ---------- 注册表（advapi32 P/Invoke） ----------

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegCreateKeyEx(IntPtr hKey, string lpSubKey, int reserved, string? lpClass, int dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegSetValueEx(IntPtr hKey, string? lpValueName, int reserved, int dwType, byte[] lpData, int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(IntPtr hKey, string? lpValueName, IntPtr lpReserved, out int lpType, byte[]? lpData, ref int lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegDeleteTree(IntPtr hKey, string lpSubKey);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MsgBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private static void SetDefaultValue(string subKey, string value) => SetValue(subKey, null, value);

    private static void SetValue(string subKey, string? valueName, string value)
    {
        if (RegCreateKeyEx(Hkcu, subKey, 0, null, 0, KeyWrite, IntPtr.Zero, out var key, out _) != ErrorSuccess)
            throw new InvalidOperationException($"无法创建注册表项：{subKey}");
        try
        {
            var data = Encoding.Unicode.GetBytes(value + '\0');
            if (RegSetValueEx(key, valueName, 0, RegSz, data, data.Length) != ErrorSuccess)
                throw new InvalidOperationException($"无法写入注册表值：{subKey}\\{(valueName ?? "(默认)")}");
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static string? GetDefaultValue(string subKey) => GetValue(subKey, null);

    private static string? GetValue(string subKey, string? valueName)
    {
        if (RegOpenKeyEx(Hkcu, subKey, 0, KeyRead, out var key) != ErrorSuccess)
            return null;
        try
        {
            var size = 0;
            if (RegQueryValueEx(key, valueName, IntPtr.Zero, out _, null, ref size) != ErrorSuccess)
                return null;
            var data = new byte[size];
            if (RegQueryValueEx(key, valueName, IntPtr.Zero, out _, data, ref size) != ErrorSuccess)
                return null;
            var len = size >= 2 ? size - 2 : 0;   // REG_SZ 以 \0 结尾，截去
            return Encoding.Unicode.GetString(data, 0, len);
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static void DeleteValue(string subKey, string valueName)
    {
        if (RegOpenKeyEx(Hkcu, subKey, 0, KeyWrite, out var key) != ErrorSuccess)
            return;
        try
        {
            RegDeleteValue(key, valueName);
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static void DeleteTree(string subKey)
    {
        RegDeleteTree(Hkcu, subKey);   // 键不存在时返回错误码，忽略
    }

    private static void Msg(string text, string title) => MsgBox(IntPtr.Zero, text, title, 0x40);

    private static int MsgYesNo(string text, string title) => MsgBox(IntPtr.Zero, text, title, 0x04 | 0x20);
}
