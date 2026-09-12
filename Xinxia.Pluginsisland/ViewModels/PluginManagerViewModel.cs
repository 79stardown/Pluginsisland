using System.Collections.ObjectModel;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using Xinxia.Pluginsisland.Services;

namespace Xinxia.Pluginsisland.ViewModels;

public sealed class PluginManagerViewModel : ObservableObjectMini
{
    private readonly PluginsislandRuntime _runtime;
    private readonly FileAssociationService _fileAssoc;

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

    public PluginManagerViewModel(PluginsislandRuntime runtime, FileAssociationService fileAssoc)
    {
        _runtime = runtime;
        _fileAssoc = fileAssoc;
    }

    public void RefreshAssociationState()
    {
        _isAssociationRegistered = _fileAssoc.IsRegistered();
        OnPropertyChanged(nameof(IsAssociationRegistered));
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
