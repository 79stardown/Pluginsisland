using System.Collections.ObjectModel;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using Xinxia.Pluginsisland.Services;

namespace Xinxia.Pluginsisland.ViewModels;

public sealed class PluginManagerViewModel : ObservableObjectMini
{
    private readonly PluginsislandRuntime _runtime;
    private readonly FileAssociationService _fileAssoc;
    private readonly InstallOptionsService _options;

    public ObservableCollection<PluginEntryViewModel> Plugins { get; } = new();

    /// <summary>宿主设置页（用于 RequestRestart）。</summary>
    public Views.PluginsislandSettingsPage? Page { get; set; }

    private bool _isAssociationRegistered;
    public bool IsAssociationRegistered
    {
        get => _isAssociationRegistered;
        set
        {
            if (SetProperty(ref _isAssociationRegistered, value))
            {
                if (value)
                    _fileAssoc.Register();
                else
                    _fileAssoc.Unregister();
            }
        }
    }

    /// <summary>安装后是否自动跳到官方插件页。值一经改动即写入注册表（经 helper），
    /// 是以此处不另存副本、每次进设置页都从注册表重读。</summary>
    private bool _isAutoOpenPluginsPage;
    public bool IsAutoOpenPluginsPage
    {
        get => _isAutoOpenPluginsPage;
        set
        {
            if (SetProperty(ref _isAutoOpenPluginsPage, value))
                _options.AutoOpenPluginsPage = value;
        }
    }

    public PluginManagerViewModel(
        PluginsislandRuntime runtime, FileAssociationService fileAssoc, InstallOptionsService options)
    {
        _runtime = runtime;
        _fileAssoc = fileAssoc;
        _options = options;
    }

    public void RefreshAssociationState()
    {
        _isAssociationRegistered = _fileAssoc.IsRegistered();
        OnPropertyChanged(nameof(IsAssociationRegistered));
    }

    /// <summary>直赋后备字段而非走属性，免得回写注册表。</summary>
    public void RefreshAutoOpenState()
    {
        _isAutoOpenPluginsPage = _options.AutoOpenPluginsPage;
        OnPropertyChanged(nameof(IsAutoOpenPluginsPage));
    }

    public void Refresh()
    {
        Plugins.Clear();
        foreach (var info in IPluginService.LoadedPlugins)
        {
            var entry = new PluginEntryViewModel(info) { HostPage = Page };
            Plugins.Add(entry);
        }
    }

    public void InstallFromLocal(IEnumerable<string> paths) =>
        _runtime.InstallFromLocal(paths, () => Page?.RequestPluginRestart());
}
