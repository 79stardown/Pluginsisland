using System.IO.Compression;
using System.Reflection;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Models.UriNavigation;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Xinxia.Pluginsisland.Services;

/// <summary>
/// 运行时中枢：AppStarted 时注册 URI 处理器，并把各插件的设置页归入「插件管理」导航组；
/// 提供从本地安装 .cipx 的管线（复制入缓存 + 请求重启，与官方同款机制）。
/// </summary>
public sealed class PluginsislandRuntime
{
    public const string PluginId = "xinxia.pluginsisland";
    public const string InstallUriPath = "xinxia.pluginsisland/install";

    /// <summary>CI 待安装插件包缓存目录（官方 PluginService.PluginsPkgRootPath 的等价路径）。</summary>
    public static string CachePkgDir => Path.Combine(CommonDirectories.AppCacheFolderPath, "PluginPackages");

    /// <summary>「插件管理」导航分组 id（须与 Plugin.cs 中 AddSettingsPageGroup 之调用一致）。</summary>
    public const string ManageGroupId = "xinxia.pluginsisland.manage";

    /// <summary>官方「插件」设置页之 URI（取自 ClassIsland 自身所用之字面量）。</summary>
    public const string PluginsSettingsUri = "classisland://app/settings/classisland.plugins";

    /// <summary>
    /// 重启以完成安装之启动参数。是否顺带跳到官方插件页，取决于设置页的
    /// 「安装后自动打开插件页」开关（<see cref="InstallOptionsService.AutoOpenPluginsPage"/>，默认关）。
    /// <para>
    /// <c>-m</c>（<c>--waitMutex</c>）不可省：<c>Restart</c> 是先 <c>Stop()</c> 再起新进程，
    /// 旧实例未必已释放互斥体；不带 <c>-m</c> 时新进程会认作「第二实例」，
    /// 把 <c>--uri</c> 转发给正在退出的旧进程后自杀，重启即静默失败。
    /// ClassIsland 自带的 <c>Restart()</c> 同样恒带 <c>-m</c>。故无 <c>--uri</c> 时也须单留 <c>-m</c>。
    /// </para>
    /// <para>
    /// <c>--uri</c> 由新实例在 <c>MainWindow.PostInit</c> 末尾处理（<c>NavigateWrapped</c>），
    /// 故冷启动亦会跳转——这正是「安装完自动打开插件页」之所依。
    /// </para>
    /// </summary>
    private string[] BuildRestartArgs() => _options.AutoOpenPluginsPage
        ? new[] { "-m", "--uri", PluginsSettingsUri }
        : new[] { "-m" };

    /// <summary>他插件设置页之 GroupId 为 internal setter，唯有反射可设。</summary>
    private static readonly PropertyInfo? GroupIdProperty = typeof(SettingsPageInfo).GetProperty(
        "GroupId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly InstallOptionsService _options;

    public PluginsislandRuntime(InstallOptionsService options) => _options = options;

    private bool _uriRegistered;
    private bool _groupingHooked;
    private ILogger? _logger;
    private bool _loggerResolved;

    private ILogger? Logger
    {
        get
        {
            if (!_loggerResolved)
            {
                _loggerResolved = true;
                try { _logger = IAppHost.TryGetService<ILogger<PluginsislandRuntime>>(); }
                catch { /* 宿主未注册该类别时静默 */ }
            }
            return _logger;
        }
    }

    /// <summary>
    /// 挂钩 AppStarted：此时全部插件 Initialize 已执行完毕、IAppHost.Host 可用、且在 UI 线程。
    /// （Initialize 阶段严禁解析任何服务——宿主尚未构建。）
    /// </summary>
    public void OnAppStarted()
    {
        Logger?.LogInformation("Pluginsisland: AppStarted 已触发，注册 URI 处理器并归组设置页。");
        if (!_uriRegistered && IAppHost.TryGetService<IUriNavigationService>() is { } nav)
        {
            nav.HandlePluginsNavigation(InstallUriPath, HandleInstallNavigation);
            _uriRegistered = true;
        }
        ApplyGrouping();
    }

    /// <summary>
    /// 将各插件自身的设置页折叠进「插件管理」导航组（成为设置窗左树中该父节点之子项）。
    /// <para>
    /// 他插件页之 <c>GroupId</c> 为 internal setter，唯有反射可设；且
    /// <c>SettingsWindowNew.BuildNavigationMenuItems()</c> 于设置窗**构造时**一次性建树，
    /// 故须在设置窗开启前（AppStarted）设毕。迟到的页由 <c>Registered.CollectionChanged</c> 兜底。
    /// </para>
    /// </summary>
    public void ApplyGrouping()
    {
        if (GroupIdProperty is null)
        {
            Logger?.LogWarning("Pluginsisland: 未找到 SettingsPageInfo.GroupId，无法归组。");
            return;
        }

        if (!_groupingHooked)
        {
            // 本插件之 Initialize 或早于他插件，其页注册于其后到达——订阅兜底，幂等无害
            SettingsWindowRegistryService.Registered.CollectionChanged += (_, _) => ApplyGrouping();
            _groupingHooked = true;
        }

        var pluginIds = GetLoadedPluginIds();
        if (Logger is { } log)
        {
            log.LogInformation("Pluginsisland: 归组开始。已装插件 id = [{Ids}]。", string.Join(", ", pluginIds));
            foreach (var info in SettingsWindowRegistryService.Registered)
                log.LogInformation(
                    "Pluginsisland: 页 {PageId}｜类别 {Category}｜原组 {Group}",
                    info.Id, info.Category, info.GroupId ?? "(无)");
        }
        foreach (var info in SettingsWindowRegistryService.Registered)
            TryMoveToManageGroup(info, pluginIds);
    }

    /// <summary>
    /// 已装插件 id 集（不含本插件）。取两处并集，防单一来源在启动早期为空：
    /// <c>IPluginService.LoadedPlugins</c> 与宿主 DI 中的 <c>PluginBase</c> 注册。
    /// </summary>
    private static HashSet<string> GetLoadedPluginIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var info in IPluginService.LoadedPlugins)
                if (!string.IsNullOrEmpty(info.Manifest.Id))
                    ids.Add(info.Manifest.Id);
        }
        catch { /* 服务未就绪则退回 DI 一路 */ }

        try
        {
            if (IAppHost.Host is { } host)
                foreach (var entry in host.Services.GetServices<PluginBase>())
                    if (!string.IsNullOrEmpty(entry.Info.Manifest.Id))
                        ids.Add(entry.Info.Manifest.Id);
        }
        catch { /* 同上 */ }

        ids.Remove(PluginId);
        return ids;
    }

    /// <summary>该页 id 是否归属某个已装插件（全等，或以「插件id.」/「插件id-」为前缀）。</summary>
    private static bool OwnedByPlugin(string pageId, HashSet<string> pluginIds)
    {
        foreach (var pid in pluginIds)
        {
            if (pageId.Equals(pid, StringComparison.Ordinal) ||
                pageId.StartsWith(pid + ".", StringComparison.Ordinal) ||
                pageId.StartsWith(pid + "-", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 把单个他插件页移入「插件管理」组；内置页与本插件自建页不动。
    /// <para>
    /// 判据是**归属**而非 id 前缀——第三方插件的 id 亦可能以 <c>classisland.</c> 起头
    /// （如样式注入器 <c>classisland.injector</c>，其设置页 id 与插件 id 全等），
    /// 单看前缀会把它们误判成内置页而漏掉。
    /// </para>
    /// </summary>
    private void TryMoveToManageGroup(SettingsPageInfo info, HashSet<string> pluginIds)
    {
        // 本插件自建页另由 [Group] 特性归组
        if (info.Id.Equals(PluginId, StringComparison.Ordinal) ||
            info.Id.StartsWith(PluginId + ".", StringComparison.Ordinal))
            return;

        var owned = OwnedByPlugin(info.Id, pluginIds);
        // 兜底：类别为「扩展页」且不似官方内置（classisland.*）者一并归组，
        // 以涵盖页 id 不合「插件id.」惯例的插件
        var shouldGroup = owned ||
                          (info.Category == SettingsPageCategory.External &&
                           !info.Id.StartsWith("classisland.", StringComparison.Ordinal));

        if (!shouldGroup)
        {
            Logger?.LogInformation(
                "Pluginsisland: 设置页 {PageId}（类别 {Category}）保留原位。", info.Id, info.Category);
            return;
        }

        if (info.GroupId == ManageGroupId)
            return;

        try
        {
            GroupIdProperty!.SetValue(info, ManageGroupId);
            Logger?.LogInformation(
                "Pluginsisland: 设置页 {PageId} 已归入「插件管理」（按归属判定={Owned}）。", info.Id, owned);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Pluginsisland: 设置页 {PageId} 归组失败。", info.Id);
        }
    }

    /// <summary>从本地安装：过滤 .cipx → 复制入缓存 → 请求重启（与官方「从本地安装」同机制）。
    /// requestRestart 由设置页传入（SettingsPageBase.RequestRestart 为 protected，外部不可直呼）。</summary>
    public void InstallFromLocal(IEnumerable<string> filePaths, Action? requestRestart)
    {
        var files = filePaths
            .Where(f => f.EndsWith(".cipx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0)
            return;

        Directory.CreateDirectory(CachePkgDir);
        var copied = 0;
        foreach (var f in files)
        {
            try
            {
                File.Copy(f, Path.Combine(CachePkgDir, Path.GetFileName(f)), true);
                copied++;
            }
            catch
            {
                // 单个文件失败（占用/消失）不影响其余
            }
        }
        if (copied > 0)
            requestRestart?.Invoke();
    }

    /// <summary>
    /// 接收 helper 经 IPC 转发的安装请求。插件包已由 helper 暂存至缓存，
    /// 此处只做校验 + 确认 + 重启（「取消」= 延后到下次启动自动安装，文案已明示）。
    /// </summary>
    private async void HandleInstallNavigation(UriNavigationEventArgs args)
    {
        try
        {
            Logger?.LogInformation("Pluginsisland: 收到安装导航请求 {Uri}。", args.Uri);

            var query = args.Uri.Query.TrimStart('?');
            string? package = null;
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "package")
                    package = Uri.UnescapeDataString(kv[1]);
            }
            if (string.IsNullOrWhiteSpace(package))
            {
                Logger?.LogWarning("Pluginsisland: 安装请求缺少 package 参数，已忽略。");
                return;
            }

            var pkgPath = Path.Combine(CachePkgDir, package);
            if (!File.Exists(pkgPath))
            {
                Logger?.LogWarning("Pluginsisland: 待装插件包不存在：{Path}", pkgPath);
                return;
            }

            var (id, name, version) = ReadPackageManifest(pkgPath);
            var title = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(package) : name;
            Logger?.LogInformation("Pluginsisland: 待装插件「{Name}」{Version}（{Id}），包={Package}", title, version ?? "(无版本)", id ?? "(未知)", package);

            var confirmed = await ShowInstallConfirmationAsync(title, id, version);
            Logger?.LogInformation("Pluginsisland: 用户在安装确认框选择了「{Choice}」。", confirmed ? "确认" : "取消");

            if (confirmed)
            {
                Logger?.LogInformation(
                    "Pluginsisland: 重启以完成安装（自动打开插件页={AutoOpen}）。", _options.AutoOpenPluginsPage);
                AppBase.Current.Restart(BuildRestartArgs());
            }
        }
        catch (Exception ex)
        {
            // 不吞异常：IPC 通道无回执，日志是唯一的线索。插件包已在缓存，下次启动仍会自动安装。
            Logger?.LogError(ex, "Pluginsisland: 处理安装导航请求时出错。");
        }
    }

    /// <summary>
    /// 弹「确认/取消」二选一对话框。
    /// <para>
    /// 用 FluentAvalonia 的 <see cref="TaskDialog"/>（自带宿主），**不可**用
    /// <c>ContentDialogHelper.ShowConfirmationDialog</c>——后者内部是
    /// <c>ContentDialog.ShowAsync(TopLevel)</c>，要求目标窗口的视觉树里存在
    /// <c>ContentDialogHost</c>，而主窗口并没有；<c>ShowAsync</c> 会抛
    /// <c>InvalidOperationException</c>，表现为「双击 .cipx 后毫无反应」。
    /// 且该辅助方法未传按钮文案时 <c>PrimaryButtonText</c>/<c>CloseButtonText</c> 皆为 null，
    /// 即便能显示也没有按钮。
    /// </para>
    /// <para>
    /// 此处照抄 ClassIsland 自身于 <c>CommonTaskDialogs.ShowDialog</c> 及
    /// <c>UriNavigationService.NavigateWrapped</c> 异常分支中的写法——二者都在同样的
    /// UI 线程上下文里以此法成功弹窗（<c>Navigate</c>/<c>NavigateWrapped</c> 由
    /// <c>Dispatcher.UIThread.Invoke</c> 包裹，故本处理函数即在 UI 线程）。
    /// </para>
    /// </summary>
    private async Task<bool> ShowInstallConfirmationAsync(string title, string? id, string? version)
    {
        var root = AppBase.Current.GetRootWindow();
        if (root is null)
        {
            Logger?.LogWarning("Pluginsisland: 主窗口不可用，无法显示安装确认框。");
            return false;
        }

        // 文案须与 BuildRestartArgs 的实际行为一致：开关关着就别承诺会打开插件页
        var outcome = _options.AutoOpenPluginsPage
            ? "确认后 ClassIsland 将立即重启以完成安装，并打开插件页；"
            : "确认后 ClassIsland 将立即重启以完成安装；";
        var text = $"即将安装插件「{title}」{(string.IsNullOrEmpty(version) ? "" : " " + version)}（{id ?? "未知"}）。\n" +
                   outcome + "\n" +
                   "若选择取消，该插件包将在下次启动 ClassIsland 时自动安装。";

        var dialog = new TaskDialog
        {
            Header = "安装插件",
            Content = text,
            XamlRoot = root,
        };
        dialog.Buttons.Add(new TaskDialogButton("取消", false));
        dialog.Buttons.Add(new TaskDialogButton("重启并安装", true) { IsDefault = true });

        // ReSharper disable once RedundantBoolCompare —— 回执是装箱的 object，须模式匹配
        return await dialog.ShowAsync(false) is true;
    }

    private static (string? Id, string? Name, string? Version) ReadPackageManifest(string pkgPath)
    {
        string? id = null, name = null, version = null;
        using var zip = ZipFile.OpenRead(pkgPath);
        var entry = zip.GetEntry("manifest.yml");
        if (entry is null)
            return (null, null, null);
        using var reader = new StreamReader(entry.Open());
        while (reader.ReadLine() is { } line)
        {
            var t = line.Trim();
            if (t.StartsWith("id:", StringComparison.OrdinalIgnoreCase)) id = TrimYaml(t["id:".Length..]);
            else if (t.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) name = TrimYaml(t["name:".Length..]);
            else if (t.StartsWith("version:", StringComparison.OrdinalIgnoreCase)) version = TrimYaml(t["version:".Length..]);
        }
        return (id, name, version);
    }

    private static string TrimYaml(string s) => s.Trim().Trim('"', '\'');
}
