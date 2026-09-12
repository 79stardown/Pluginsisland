using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using Xinxia.Pluginsisland.Services;
using Xinxia.Pluginsisland.ViewModels;

namespace Xinxia.Pluginsisland.Views;

[SettingsPageInfo("xinxia.pluginsisland.settings", "已装插件", SettingsPageCategory.External)]
[Group("xinxia.pluginsisland.manage")]
public partial class PluginsislandSettingsPage : SettingsPageBase
{
    private readonly FileAssociationService _fileAssoc;
    private readonly PluginManagerViewModel _vm;

    public PluginsislandSettingsPage(
        PluginsislandRuntime runtime, FileAssociationService fileAssoc, InstallOptionsService options)
    {
        _fileAssoc = fileAssoc;
        _vm = new PluginManagerViewModel(runtime, fileAssoc, options) { Page = this };
        DataContext = _vm;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _vm.Refresh();
        _vm.RefreshAssociationState();
        _vm.RefreshAutoOpenState();
    }

    /// <summary>供 PluginBlock 调用：弹官方「需要重启」对话框。</summary>
    public void RequestPluginRestart() => RequestRestart();

    /// <summary>供 PluginBlock 卸载本插件前调用：先注销 .cipx 文件关联。</summary>
    public void UnregisterSelfAssociation() => _fileAssoc.UnregisterIfSelf();

    private async void ButtonInstallFromLocal_OnClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要安装的插件包",
            AllowMultiple = true,
            FileTypeFilter = new[] { IPluginService.PluginPackageFileType },
        });
        if (files.Count == 0)
            return;
        _vm.InstallFromLocal(files.Select(GetLocalPath));
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.Data.GetFiles()?.Select(GetLocalPath) ?? Enumerable.Empty<string>();
        _vm.InstallFromLocal(paths);
    }

    private static string GetLocalPath(IStorageItem item) =>
        item.TryGetLocalPath() ?? item.Path.LocalPath;
}
