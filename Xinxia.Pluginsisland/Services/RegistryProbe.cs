using System.Runtime.InteropServices;
using System.Text;

namespace Xinxia.Pluginsisland.Services;

/// <summary>
/// 极简注册表读取（advapi32 P/Invoke，零 NuGet——避免与宿主依赖副本冲突）。
/// <para>
/// 只读不写：所有写入一律交给 <c>Pluginsisland.Helper.exe</c>，
/// 以维持「helper 独占 <c>HKCU\Software\xinxia.pluginsisland</c>」这一分工。
/// </para>
/// </summary>
internal static class RegistryProbe
{
    private static readonly IntPtr Hkcu = new(0x80000001);
    private const int KeyRead = 0x20019;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(IntPtr hKey, string? lpValueName, IntPtr lpReserved, out int lpType, byte[]? lpData, ref int lpcbData);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    /// <summary>读字符串值；键或值不存在时返回 <c>null</c>。valueName 为 null 表示读默认值。</summary>
    public static string? GetValue(string subKey, string? valueName)
    {
        if (RegOpenKeyEx(Hkcu, subKey, 0, KeyRead, out var key) != 0)
            return null;
        try
        {
            var size = 0;
            if (RegQueryValueEx(key, valueName, IntPtr.Zero, out _, null, ref size) != 0)
                return null;
            var data = new byte[size];
            if (RegQueryValueEx(key, valueName, IntPtr.Zero, out _, data, ref size) != 0)
                return null;
            var len = size >= 2 ? size - 2 : 0;   // REG_SZ 以 \0 结尾，截去
            return Encoding.Unicode.GetString(data, 0, len);
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    public static string? GetDefaultValue(string subKey) => GetValue(subKey, null);
}
