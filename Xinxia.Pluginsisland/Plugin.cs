using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xinxia.Pluginsisland.Services;
using Xinxia.Pluginsisland.Views;

namespace Xinxia.Pluginsisland;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 导航分组「插件管理」：各插件之设置页皆折叠为此组之子项
        // （他插件页面的 GroupId 为 internal setter，注册后由 PluginsislandRuntime 以反射设置）
        services.AddSettingsPageGroup("xinxia.pluginsisland.manage", "\uE71D", "插件管理");

        // 「已装插件」管理页（官方「插件」页不动，本页与其并存）
        services.AddSettingsPage<PluginsislandSettingsPage>();
        services.AddSingleton<PluginsislandRuntime>();
        services.AddSingleton<FileAssociationService>();
        services.AddSingleton<InstallOptionsService>();

        // 挂钩 AppStarted：此时全部插件 Initialize 已执行、IAppHost.Host 已可用、且在 UI 线程。
        // （Initialize 阶段宿主尚未构建，严禁解析任何服务。）
        if (AppBase.Current is { } app)
            app.AppStarted += (_, _) => IAppHost.TryGetService<PluginsislandRuntime>()?.OnAppStarted();
    }
}
