using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Xinxia.Pluginsisland.ViewModels;

/// <summary>
/// 最小 INotifyPropertyChanged 实现。不引 CommunityToolkit.Mvvm——
/// 宿主（ClassIsland）已自带一份，插件目录再放副本会在 PluginLoadContext 中二次加载。
/// </summary>
public abstract class ObservableObjectMini : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
