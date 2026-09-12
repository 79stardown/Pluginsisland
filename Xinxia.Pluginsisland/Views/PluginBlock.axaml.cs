using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Xinxia.Pluginsisland.ViewModels;

namespace Xinxia.Pluginsisland.Views;

public partial class PluginBlock : UserControl
{
    public PluginBlock()
    {
        InitializeComponent();
    }

    private PluginEntryViewModel? Vm => DataContext as PluginEntryViewModel;

    /// <summary>卸载需重启生效，走设置窗口根的 RequestRestart 命令绑定（弹官方重启对话框）。</summary>
    private void ButtonUninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm is null)
            return;
        // 若卸载的是本插件，先注销 .cipx 文件关联，防残留指向已删除的 exe
        if (vm.Info.Manifest.Id == Services.PluginsislandRuntime.PluginId)
            vm.HostPage?.UnregisterSelfAssociation();
        vm.Info.IsUninstalling = true;
        vm.HostPage?.RequestPluginRestart();
    }

    private void ButtonUndoUninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            vm.Info.IsUninstalling = false;
    }
}

/// <summary>字符串文件路径 → Bitmap（插件图标）。</summary>
public sealed class StringToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
