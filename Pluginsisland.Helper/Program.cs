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

    /// <summary>官方「插件」设置页之 URI。与 Xinxia.Pluginsisland 中的同名常量须保持一致
    /// （helper 不能反向引用插件工程，否则形成循环引用）。</summary>
    private const string PluginsSettingsUri = "classisland://app/settings/classisland.plugins";

    /// <summary>「安装后自动打开插件页」开关之值名，存于 <see cref="AuthorKey"/> 之下，
    /// 取值 <c>"1"</c>/<c>"0"</c>，**缺省（值不存在）即关**。
    /// 须与插件 InstallOptionsService.AutoOpenValueName 一致。</summary>
    private const string AutoJumpValueName = "AutoOpenPluginsPage";

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
                "--set-auto-jump" => SetAutoJump(GetArgValue(args, "--set-auto-jump")),
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

    // ---------- 安装行为开关 ----------

    /// <summary>
    /// 设定「安装后自动打开插件页」，由插件设置页的开关调用。**静默**：成功时不弹提示框，
    /// 否则每次拨动开关都会蹦一个窗。
    /// </summary>
    private static int SetAutoJump(string? value)
    {
        SetValue(AuthorKey, AutoJumpValueName, value == "1" ? "1" : "0");
        return 0;
    }

    /// <summary>读「安装后自动打开插件页」；值不存在即 false（默认关）。</summary>
    private static bool GetAutoJump() => GetValue(AuthorKey, AutoJumpValueName) == "1";

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
            // 分支 B：CI 未运行。
            //
            // ⚠ 提示框必须弹在启动 CI **之前**，此顺序不可调换。
            // helper 自身就住在插件目录里（即 <CI>\data\Plugins\xinxia.pluginsisland\），进程存活
            // 期间 Windows 锁着 Pluginsisland.Helper.exe / .dll。而 CI 装包走的是
            // PluginService.ProcessPluginsInstall：
            //     Directory.Delete(插件目录, recursive: true)
            //   → Directory.CreateDirectory
            //   → ZipFile.ExtractToDirectory
            // 删到上述被占用的文件时抛异常，其后两步一概不执行：目录已被删剩一半、插件当场报废。
            // 更糟的是该异常只被 Console.WriteLine 吞掉（不进日志文件），且紧随其后的
            // File.Delete(pkg) 照样执行——包也被删了，事后毫无线索可查。
            // 故先把提示框弹完；用户点掉后本进程随即退出、占用释放，CI 才开始启动，
            // 而 CI 从启动到执行装包有数秒之遥，届时占用早已不存在。
            Msg($"已暂存插件「{package}」{(string.IsNullOrEmpty(version) ? "" : " " + version)}（{id}），ClassIsland 即将启动并安装。", "Pluginsisland 安装助手");

            // 是否顺带 --uri 跳到插件页，取决于插件设置页里的开关（默认关）；
            // 打开时由 CI 在 MainWindow.PostInit 末尾处理 --uri，于是装完即落在插件页上。
            // （双击 .cipx 本身即是同意安装，此处无需再弹确认框。）
            var autoJump = GetAutoJump();
            Process.Start(new ProcessStartInfo
            {
                FileName = ciExe,
                WorkingDirectory = ciRoot!,
                UseShellExecute = true,
                Arguments = autoJump ? $"--uri \"{PluginsSettingsUri}\"" : "",
            });
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
