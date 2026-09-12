# Pluginsisland

#  注：本插件代码部分使用了AI辅助，已经过人工测试。

ClassIsland 的插件管理增强插件（插件 id：`xinxia.pluginsisland`）。

把散落在设置窗口左侧导航树中的各插件设置页，统一折叠到一个「插件管理」分组之下；
并提供一个集中的已装插件管理页（启停 / 卸载 / 从本地安装），以及 `.cipx` 插件包的双击安装。

## 功能

### 「插件管理」导航分组

设置窗口左侧导航树中新增一个可折叠的「插件管理」父节点，其下包含：

- **已装插件** —— 每个已安装插件一张可折叠卡片：图标、名称、版本、作者、加载状态，
  以及启用 / 停用开关、卸载 / 取消卸载
- **各插件自身的设置页** —— 自动归入本分组之下，不再散落于导航树顶层

ClassIsland 自身的内置页（应用、组件、外观、关于……）与官方「插件」页**保持原样不动**。

### 从本地安装

「已装插件」页内提供「从本地安装 `.cipx` 插件包」，也可直接把 `.cipx` 文件拖放到该页面。
安装机制与官方一致：插件包复制入缓存目录后重启 ClassIsland 完成安装。

### 双击 `.cipx` 安装

在「已装插件」页开启「注册 `.cipx` 文件关联」后，双击 `.cipx` 插件包即可安装：

- ClassIsland **运行中** —— 弹出确认对话框，确认后重启并安装；取消则下次启动时自动安装
- ClassIsland **未运行** —— 插件包暂存至缓存目录并拉起 ClassIsland，启动时自动安装

文件关联仅写入当前用户注册表（`HKCU`），无需管理员权限。卸载本插件前请先关闭文件关联
（卸载按钮会先自动注销，避免残留指向已删除的程序）。

> 双击安装依赖随插件分发的 `Pluginsisland.Helper.exe`。该程序零第三方依赖、无网络访问、无开机自启，
> 仅读写 `HKCU` 注册表与本地文件。若被安全软件拦截请放行。

## 安装

1. 取得 `Xinxia.Pluginsisland.cipx`
2. 在 ClassIsland 中打开「插件」页，选择从本地安装；或直接双击该 `.cipx` 文件
3. 按提示重启 ClassIsland

要求 ClassIsland **2.1.0.1**（`apiVersion 2.0.0.0`）。

## 从源码构建

需要 .NET 8 SDK（本机验证于 8.0.421）。

```bash
dotnet build Xinxia.Pluginsisland/Xinxia.Pluginsisland.csproj -c Release
```

产物：

- 可部署目录 —— `Xinxia.Pluginsisland/bin/Release/net8.0/`
- 插件包 —— `Xinxia.Pluginsisland/cipx/Xinxia.Pluginsisland.cipx`

本地调试：把上述**可部署目录的内容**复制到 `<ClassIsland 根目录>/data/Plugins/xinxia.pluginsisland/`，
重启 ClassIsland 即生效。

## 项目结构

```
Pluginsisland\
├── Pluginsisland.sln
├── Directory.Build.targets              # 构建环境适配
├── assets\                              # 图标源文件（改图用）
│   ├── plugin-icon.ico                  #   插件图标源（→ Xinxia.Pluginsisland\icon.png）
│   └── cipx-icon.ico                    #   .cipx 关联图标源（→ Pluginsisland.Helper\App.ico）
├── tools\make-icon.ps1                  # 由图标源生成多尺寸 .ico
├── Pluginsisland.Helper\                # .cipx 安装助手（零 NuGet 依赖的 WinExe）
│   ├── Program.cs                       #   注册表 advapi32 P/Invoke + user32 MessageBoxW
│   └── App.ico
└── Xinxia.Pluginsisland\                # 插件主体
    ├── manifest.yml  icon.png  README.md
    ├── Plugin.cs                        #   入口：注册分组、设置页与 AppStarted 挂钩
    ├── Services\                        #   运行时中枢（归组 / URI 安装管线）与文件关联
    ├── ViewModels\
    └── Views\
```

### 换图标

- **插件图标**：替换 `Xinxia.Pluginsisland/icon.png` 即可（清单的 `Icon` 字段默认为 `icon.png`，无需改 manifest）
- **`.cipx` 关联图标**：更新 `assets/cipx-icon.ico` 后执行

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 `
      -Source assets\cipx-icon.ico -Destination Pluginsisland.Helper\App.ico
  ```

  随后重新构建。文件关联的注册表值指向部署目录中的 `App.ico`，路径不变，故**换图不必重新注册**，
  必要时执行 `ie4uinit.exe -show` 刷新 Windows 图标缓存。

## 实现要点

设置页分组依赖两处 ClassIsland 行为，供二次开发者参考：

- `services.AddSettingsPageGroup(id, icon, name)` 注册分组，本插件自身的设置页用 `[Group(id)]` 特性归入
- **他插件的设置页**，其 `SettingsPageInfo.GroupId` 是 `internal set`，**只能经反射写入**；
  且导航树在设置窗口**构造时**一次性建成，故必须在设置窗口开启前（`AppStarted`）完成——
  迟到的页由 `SettingsWindowRegistryService.Registered.CollectionChanged` 兜底

> ⚠ 判断某个设置页是否属于插件，**不能看 id 前缀**。ClassIsland 内置页的 id 是无前缀的裸名
> （`general`、`components`、`about`……），而第三方插件的 id 却可能以 `classisland.` 开头
> （如样式注入器 `classisland.injector`）。本插件按**归属**判定：页 id 与某已装插件 id 全等，
> 或以其加 `.` / `-` 为前缀。

## 依赖

| 依赖 | 版本 | 用途 | 许可证 |
|---|---|---|---|
| [ClassIsland.PluginSdk](https://www.nuget.org/packages/ClassIsland.PluginSdk) | 2.1.0.1（**锁定**） | ClassIsland 插件 SDK | LGPL-3.0-only |

`Pluginsisland.Helper` 不引用任何 NuGet 包，仅用 BCL 与 Win32 P/Invoke。

> SDK 版本**必须锁 2.1.0.1**：2.1.1.x 起仅支持 `net10.0`，浮动版本 `2.1.*` 会解析到 2.1.1.1 导致 NU1202。

## 许可

[MIT](LICENSE)
