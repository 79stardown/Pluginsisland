using ClassIsland.Core.Models.Plugin;
using Xinxia.Pluginsisland.Views;

namespace Xinxia.Pluginsisland.ViewModels;

/// <summary>单个已安装插件的展示模型：Info 直接暴露绑定（PluginInfo 自带 INPC）。
/// 插件自身的设置页**不在**卡片内嵌，而由 PluginsislandRuntime 归入设置窗左侧「插件管理」导航组。</summary>
public sealed class PluginEntryViewModel : ObservableObjectMini
{
    public PluginInfo Info { get; }

    /// <summary>宿主设置页（RequestRestart / 卸载前注销文件关联经由它）。</summary>
    public PluginsislandSettingsPage? HostPage { get; set; }

    public string VersionAndAuthor { get; }
    public string StatusText { get; }
    public bool IsError { get; }
    public string ErrorText { get; }

    public PluginEntryViewModel(PluginInfo info)
    {
        Info = info;

        var m = info.Manifest;
        var author = string.IsNullOrWhiteSpace(m.Author) ? "" : " · " + m.Author.Trim();
        VersionAndAuthor = $"v{m.Version}{author}";

        // LoadStatus 是 internal set 且不触发通知，快照时以字符串形态读取（不引用枚举类型，免命名空间依赖）
        StatusText = info.LoadStatus.ToString() switch
        {
            "Loaded" => "已加载",
            "Disabled" => "已禁用",
            "Error" => "加载失败",
            _ => "未加载",
        };
        IsError = info.LoadStatus.ToString() == "Error";
        ErrorText = info.Exception?.Message ?? "";

        // 启停开关切换后提示重启（PluginInfo.IsEnabled 的 setter 会写/删 .disabled 文件）
        info.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginInfo.IsEnabled))
                HostPage?.RequestPluginRestart();
        };
    }
}
