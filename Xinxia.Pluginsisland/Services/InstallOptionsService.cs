using System.Diagnostics;

namespace Xinxia.Pluginsisland.Services;

/// <summary>
/// 安装行为开关。目前只有一项：「安装 .cipx 后是否自动打开官方插件页」。
/// <para>
/// 值存于 <c>HKCU\Software\xinxia.pluginsisland\AutoOpenPluginsPage</c>（<c>"1"</c>/<c>"0"</c>），
/// **缺省即关**。之所以用注册表而非本插件目录下的文件：插件目录在每次安装 .cipx 时被整目录
/// 替换，存那儿会被反复重置；注册表与 <c>CiRoot</c> 一样能长期存活。
/// </para>
/// <para>
/// <c>Pluginsisland.Helper.exe</c> 亦须读此值（CI 未运行时由它决定拉起时带不带 <c>--uri</c>）。
/// helper 不能反向引用本插件工程（循环引用），故两侧各自硬编码键名——**改动须同步**。
/// </para>
/// <para>
/// 写入委托 helper（<c>--set-auto-jump</c>），本类自身只读；与
/// <see cref="FileAssociationService"/> 同一分工，确保 helper 独占用注册表键。
/// </para>
/// </summary>
public sealed class InstallOptionsService
{
    /// <summary>helper 与插件共用的注册表根（须与 helper 的 AuthorKey 一致）。</summary>
    public const string AuthorKey = @"Software\xinxia.pluginsisland";

    /// <summary>值名（须与 helper 的 AutoJumpValueName 一致）。</summary>
    public const string AutoOpenValueName = "AutoOpenPluginsPage";

    private readonly FileAssociationService _fileAssoc;

    public InstallOptionsService(FileAssociationService fileAssoc) => _fileAssoc = fileAssoc;

    /// <summary>
    /// 安装 .cipx 后是否重启并跳到官方插件页。默认 <c>false</c>：
    /// 装完只是静默重启回原样，想要的用户自行来设置页打开。
    /// </summary>
    public bool AutoOpenPluginsPage
    {
        get => RegistryProbe.GetValue(AuthorKey, AutoOpenValueName) == "1";
        set
        {
            var helper = _fileAssoc.HelperPath;
            if (!File.Exists(helper))
                return;
            Process.Start(new ProcessStartInfo
            {
                FileName = helper,
                Arguments = $"--set-auto-jump {(value ? "1" : "0")}",
                UseShellExecute = false,
            });
        }
    }
}
