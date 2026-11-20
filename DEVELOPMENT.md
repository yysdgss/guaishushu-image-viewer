# 怪叔叔看图 · 严谨开发档案（唯一权威）

> 本文件是“怪叔叔看图”项目的**唯一权威开发档案**。任何 AI 或开发者在继续开发、重构、排错、重建版本时，必须首先完整读取本文件，并以此处记录的状态、实现逻辑、代码片段、文件路径和约束为准。
>
> **强制维护规则**
> 1. 每一次修改源码、资源、配置、构建脚本、发布物、文档或外部仓库状态后，必须同步更新本文件。
> 2. 每条需求必须保持：需求原文/摘要 → 实现状态 → 实现逻辑 → 实现方式 → 关键代码片段 → 所在文件。
> 3. 代码片段必须与当前源码一致；若代码重构，本档案必须同批次改写，禁止残留旧片段。
> 4. 新增需求必须分配新的 `Rxxx` 编号，并追加到“版本变更记录”。
> 5. 若某需求被废弃，不得直接删除，必须标记 `已废弃` 并写明替代实现。
> 6. 发布版本前必须核对：源码版本号、`AppInfo.Version`、`.csproj`、托盘状态、关于窗口、README、Release 资产、本档案中的版本记录完全一致。

---

## 0. 项目身份与环境

| 项目 | 当前值 |
| --- | --- |
| 软件名称 | 怪叔叔看图 |
| 产品常量 | `AppInfo.Name = "怪叔叔看图"` |
| 程序集名 | `怪叔叔看图`（安装器：`怪叔叔看图安装程序`）|
| 当前版本 | `4.5.2` |
| 仓库 | `https://github.com/yysdgss/guaishushu-image-viewer` |
| Release | `v4.5.2` 已发布（上一版 `v4.5.1`）：https://github.com/yysdgss/guaishushu-image-viewer/releases/tag/v4.5.2 |
| 许可 | MIT |
| 目标框架 | `net10.0-windows10.0.19041.0`（PDF 渲染需要 Windows 10 19041 SDK API） |
| UI 框架 | WPF |
| 辅助框架 | Windows Forms（仅用于 `NotifyIcon`、`Screen`、`ColorDialog`） |
| 目标系统 | Windows 11 / Windows 10 x64 |
| 发布形式 | framework-dependent single-file，需 .NET 10 Desktop Runtime |
| 命名空间 | `WBImageViewer` |
| 源码目录 | `src/WBImageViewer/` |

### 0.1 项目文件

`src/WBImageViewer/WBImageViewer.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>GuaishushuViewer</AssemblyName>
    <RootNamespace>WBImageViewer</RootNamespace>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <Version>4.1.0</Version>
    <AssemblyVersion>1.2.0.0</AssemblyVersion>
    <FileVersion>1.2.0.0</FileVersion>
    <Product>怪叔叔看图</Product>
    <Authors>怪叔叔</Authors>
  </PropertyGroup>

  <ItemGroup>
    <Resource Include="Assets\app.ico" />
    <Resource Include="Assets\donate.png" />
  </ItemGroup>

</Project>
```

> 注意：`Assets/grab.cur` 曾用于自绘抓取手势光标，现按用户最新要求废弃并删除；拖动时使用 Windows 系统内置 `Cursors.Hand`，不再加载任何自定义 `.cur` 资源。

### 0.2 产品标识与可调参数

`src/WBImageViewer/Models/AppInfo.cs`

```csharp
namespace WBImageViewer.Models;

public static class AppInfo
{
    public const string Name = "怪叔叔看图";
    public const string Version = "1.2.0";
    public const string GitHub = "https://github.com/yysdgss/guaishushu-image-viewer";
}

public sealed class ImageAdjustments
{
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int Saturation { get; set; }
    public int Denoise { get; set; }
    public int Sharpen { get; set; }

    public bool IsNeutral => Brightness == 0 && Contrast == 0 && Saturation == 0 &&
                             Denoise == 0 && Sharpen == 0;

    public static ImageAdjustments SmartPreset() => new()
    {
        Brightness = 4,
        Contrast = 12,
        Saturation = 8,
        Denoise = 18,
        Sharpen = 32
    };

    public ImageAdjustments Clone() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Denoise = Denoise,
        Sharpen = Sharpen
    };
}
```

`src/WBImageViewer/Models/AppSettings.cs`

```csharp
using System.Text.Json.Serialization;

namespace WBImageViewer.Models;

public enum WindowMoveMode
{
    RightButton,
    MiddleButton,
    SpaceAndLeftButton
}

public sealed class AppSettings
{
    public bool SingleWindow { get; set; } = true;
    public string BackgroundColor { get; set; } = "#101216";
    public double PanelOpacity { get; set; } = 1.0;
    public bool AutoOptimize { get; set; } = true;
    public ImageAdjustments Adjustments { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowMoveMode MoveMode { get; set; } = WindowMoveMode.RightButton;

    public string ToggleTopMostHotkey { get; set; } = "Shift+Ctrl+Alt+T";
    public string FitWindowToImageHotkey { get; set; } = "Shift+Ctrl+Alt+R";
    public string FitImageToWindowHotkey { get; set; } = "Shift+Ctrl+Alt+S";
    public string WheelWindowModifiers { get; set; } = "Ctrl";
    public double ZoomStep { get; set; } = 1.2;
    public int CornerRadius { get; set; } = 4;
    public bool AssociateAsDefaultViewer { get; set; } = true;
    public Dictionary<string, string?> PreviousAssociations { get; set; } = new();
}
```

设置文件路径：

```csharp
// Services/SettingsService.cs
public static string SettingsDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WBImageViewer");

public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");
```

### 0.3 目录职责

| 路径 | 职责 |
| --- | --- |
| `src/WBImageViewer/App.xaml(.cs)` | 启动、设置加载、托盘菜单、单/多窗口调度、参数处理 |
| `src/WBImageViewer/MainWindow.xaml(.cs)` | 无边框面板、图片显示/缩放/平移、刘海、关闭按钮、调整面板 |
| `src/WBImageViewer/SettingsWindow.xaml(.cs)` | 后台设置与快捷键录制 |
| `src/WBImageViewer/AboutWindow.xaml(.cs)` | 版本、功能、操作说明、打赏二维码 |
| `src/WBImageViewer/Models/` | 设置模型、产品信息、调整参数 |
| `src/WBImageViewer/Services/` | 设置持久化、热键、管道、文件关联、图片增强 |
| `src/WBImageViewer/Native/Win32.cs` | Win32 常量、结构体、P/Invoke |
| `src/WBImageViewer/Assets/app.ico` | 应用与托盘图标 |
| `src/WBImageViewer/Assets/donate.png` | 打赏二维码 |
| `src/WBInstaller/` | 五步 Windows 安装向导，内嵌主程序、中文许可与卸载器 |
| `src/WBUninstaller/` | 独立卸载工具：清理文件、快捷方式、关联、配置与卸载注册信息 |
| `packaging/install.cmd` | 旧版安装脚本（历史保留） |
| `封装单文件运行包.bat` | 生成单文件 exe |
| `封装安装包.bat` | 生成带独立卸载器的安装包 exe |
| `.github/ISSUE_TEMPLATE/` | GitHub Issue 模板 |
| `DEVELOPMENT.md` | 本开发档案 |
| `README.md` | 用户说明 |
| `LICENSE` | MIT 许可（英文原文） |
| `LICENSE.zh-CN.txt` | 安装程序展示用中文 MIT 许可 |
| `DONATE.md` | 打赏说明 |
| `.workbuddy/memory/` | WorkBuddy 会话级开发日志 |

---

## 1. 需求实现索引

| 编号 | 需求摘要 | 状态 | 主要文件 |
| --- | --- | --- | --- |
| R01 | 无窗口，以面板方式显示图片 | 已实现 | `MainWindow.xaml` |
| R02 | 无多余界面按钮，极简展示图片 | 已实现（交互控件默认隐藏于刘海/顶部悬停区） | `MainWindow.xaml` |
| R03 | 图片置顶，默认 `Shift+Ctrl+Alt+T` | 已实现 | `MainWindow.xaml.cs`, `HotkeyHost.cs` |
| R04 | 滚轮缩放图片，窗口不变，以鼠标为中心 | 已实现 | `MainWindow.xaml.cs` |
| R05 | `Shift+Ctrl+Alt+R` 窗口适配图片 | 已实现 | `MainWindow.xaml.cs` |
| R06 | `Shift+Ctrl+Alt+S` 图片等比适配窗口 | 已实现 | `MainWindow.xaml.cs` |
| R07 | 注入系统，默认图片浏览器，双击打开 | 已实现为注册候选 + 系统确认 | `FileAssociationService.cs` |
| R08 | 单窗口/多窗口，默认单窗口 | 已实现 | `App.xaml.cs`, `ImagePipe.cs` |
| R09 | 四边四角原生窗口缩放 | 已实现 | `WindowChrome`, `Win32.cs` |
| R10 | 可配置移动窗口方式，默认右键 | 已实现 | `AppSettings.cs`, `MainWindow.xaml.cs` |
| R11 | 最低硬件资源渲染 | 已实现 | `MainWindow.xaml.cs`, `ImageEnhanceService.cs` |
| R12 | 窗口不活动时尽量释放资源但仍置顶 | 已实现（降低插值质量，无循环） | `MainWindow.xaml.cs` |
| R13 | Windows 最合适技术栈 | 已实现 | `.csproj` |
| R14 | 修饰键 + 滚轮同步缩放窗口 | 已实现 | `MainWindow.xaml.cs` |
| R15 | 左键拖动平移图片 | 已实现 | `MainWindow.xaml.cs` |
| R16 | 默认关联失效修复 | 已实现 | `FileAssociationService.cs` |
| R17 | 拖入 jpg/png/bmp 等图片失效修复 | 已实现 | `MainWindow.xaml(.cs)` |
| R18 | 顶部白边移除 | 已实现 | `MainWindow.xaml` |
| R19 | 圆角过大修复与后台设置 | 已实现 | `MainWindow.xaml.cs`, `SettingsWindow.xaml` |
| R20 | 图标与托盘 logo 修复 | 已实现 | `app.ico`, `App.xaml.cs` |
| R21 | 滚轮缩放锚点修复 | 已实现 | `ZoomAt` |
| R22 | 右键移动改为抓取手势 | 已实现为系统 `Cursors.Hand` | `MainWindow.xaml.cs` |
| R23 | 单文件运行包 bat | 已实现 | `封装单文件运行包.bat` |
| R24 | 安装包 bat + 安装后注册系统与默认方式 | 已实现 | `封装安装包.bat`, `packaging/install.cmd` |
| R25 | 右上角悬停展开关闭按钮 | 已实现 | `MainWindow.xaml(.cs)` |
| R26 | 边缘锯齿优化 | 已实现（DWM 圆角，无 GDI region） | `ApplyCornerRadius` |
| R27 | GitHub 建仓、项目管理、下载 | 已实现 | 仓库与 Release |
| R28 | 软件/仓库更名为“怪叔叔看图” | 已实现 | `AppInfo.cs`, `.csproj` |
| R29 | 版本号、关于界面、托盘版本状态 | 已实现 | `AboutWindow`, `App.xaml.cs` |
| R30 | 开发档案、MIT 开源许可 | 已实现 | `DEVELOPMENT.md`, `LICENSE` |
| R31 | 开源档案与关于界面打赏二维码 | 已实现 | `README.md`, `AboutWindow.xaml`, `donate.png` |
| R32 | 顶部斜刘海控制条 | 已实现 | `MainWindow.xaml` |
| R33 | 后台背景色与透明度设置 | 已实现 | `AppSettings.cs`, `SettingsWindow`, `ApplyVisualSettings` |
| R34 | 去噪、锐化、清晰度增强 | 已实现 | `ImageEnhanceService.cs` |
| R35 | PS 式亮度/对比度/色差调整与智能优化/还原 | 已实现 | `ImageEnhanceService.cs`, `AdjustPanel` |
| R36 | 严格开发档案：每项需求附实现逻辑/方式/代码且持续同步 | 本文件 | `DEVELOPMENT.md` |
| R37 | 按截图构建刘海：平时白色细线，悬停向下平滑展开 | 已实现 | `MainWindow.xaml(.cs)` |
| R38 | 图片悬停保持系统指针；左键拖动时使用系统抓取手型 | 已实现 | `MainWindow.xaml(.cs)` |
| R39 | 封装产物文件名包含版本号 | 已实现 | `封装单文件运行包.bat`, `封装安装包.bat` |
| R40 | 窗体背景透明，但图片保持不透明 | 已实现 | `MainWindow.xaml(.cs)`, `SettingsWindow.xaml(.cs)` |
| R41 | 软件名称更正为“怪叔叔看图” | 已实现 | 全项目 |
| R42 | 托盘右键菜单软件名与版本分行 | 已实现 | `App.xaml.cs` |
| R43 | 置顶改为图钉并并入右上角关闭伸缩区 | 已实现 | `MainWindow.xaml(.cs)` |
| R44 | 图片画布始终裁切到窗口当前客户区 | 已实现 | `MainWindow.xaml(.cs)` |
| R45 | 按截图重建灵动岛：上方向两侧外扩、内圆角肩部、底部收拢圆角 | 已由 R46 替代 | `MainWindow.xaml` |
| R46 | 按 ASCII 模型重构灵动岛：顶部双翼外伸、内圆角肩部、直边、底部收拢圆角 | 已废弃，由 R53 替代 | `MainWindow.xaml` |
| R47 | 窗口适配图片后画布无留白、窗口四边与图片重合 | 已实现 | `MainWindow.xaml.cs` |
| R48 | 置顶图钉改为 📌 Emoji 样式 | 已实现 | `MainWindow.xaml` |
| R49 | 灵动岛顶边与窗口顶边相切、无外伸尾部，顶角与底角圆角等大 | 已由 R53 替代 | `MainWindow.xaml` |
| R50 | 清除源码以外生成产物，仓库只保留源码与构建脚本 | 已实现 | 全项目、`.gitignore` |
| R51 | 建立专用封装目录；两个封装 bat 固定、版本号单一来源 | 已实现 | `artifacts/`、两个 bat、`WBImageViewer.csproj` |
| R52 | 置顶图钉随状态变亮/变暗、呈现钉入/拔起 | 已实现 | `MainWindow.xaml(.cs)` |
| R53 | 灵动岛右侧顶角镜像左侧并缩小整体尺寸 | 已实现 | `MainWindow.xaml` |
| R54 | 窗口适配图片时以图片的屏幕位置为锚点 | 已实现 | `MainWindow.xaml.cs`、`Win32.cs` |
| R55 | 双向适配按钮改为双层形象图标，目标层在上 | 已实现 | `MainWindow.xaml` |
| R56 | 置顶/关闭按钮顶部齐平窗口，外框用灵动岛方式构建 | 已实现 | `MainWindow.xaml(.cs)` |
| R57 | 生成常规 Windows exe 安装程序并安装为默认打开候选 | 已实现 | `src/WBInstaller/`、`封装安装包.bat` |
| R58 | 扩展图片格式并用系统缩略图支持流程图/脑图 | 已实现 | `SupportedFormats.cs`、`ShellPreviewService.cs` |
| R59 | 单文件封装只产出一个名称+版本号 exe，安装包封装只产出 exe 安装程序 | 已实现 | 两个封装 bat |
| R60 | 置顶与关闭两个按钮合并到同一个岛形面板 | 已实现 | `MainWindow.xaml(.cs)` |
| R61 | 图钉钉入/拔出加入位移、旋转、明暗与光晕动画 | 已实现 | `MainWindow.xaml(.cs)` |
| R62 | 安装程序改为常规 Windows 五步安装向导 | 已实现 | `src/WBInstaller/Program.cs`、`WBInstaller.csproj` |
| R63 | 重建安装向导：修复下一步灰显，加入实时校验、分阶段日志与错误详情 | 已实现 | `src/WBInstaller/Program.cs` |
| R64 | 安装界面改为左侧步骤导航＋右侧内容＋底部状态栏布局 | 已实现 | `src/WBInstaller/Program.cs` |
| R65 | 重做主题 Logo 并全面应用到窗口、托盘、任务栏、资源管理器、任务管理器与安装程序 | 已实现 | `Assets/app.ico`、`Assets/app.png`、各窗口与项目配置 |
| R66 | 修复 1.6.0 单文件无界面/无托盘：MultiTrigger 承载 Binding 导致窗口模板加载异常 | 已修复 | `MainWindow.xaml` |
| R67 | 用 TableLayoutPanel 重建安装主体，修复左侧步骤区覆盖右侧内容区 | 已修复 | `src/WBInstaller/Program.cs` |
| R68 | 静默写入 UserChoice 哈希自动成为默认程序，并注入图片右键菜单 | 已实现 | `DefaultAssociationService.cs`、安装程序、设置窗口 |
| R69 | 图片标注与 SolidWorks 风格罗盘菜单（含橡皮擦全套功能与可配置触发键） | 已实现 | `AnnotationModels.cs`、`MainWindow.xaml(.cs)`、`AppSettings.cs` |

---

## 2. 显示与窗口行为

### R01 无窗口，以面板方式显示图片

**需求原文摘要**：没有窗口，以一块面板的方式显示图片。

**实现状态**：已实现。

**实现逻辑**：窗口本身是真实 Win32 HWND（必须如此才能置顶、缩放、接收拖拽），但 UI 层通过 `WindowStyle=None`、`WindowChrome` 与非客户区按钮关闭，把所有传统窗口元素去掉，视觉上就是一块浮动面板。

**实现方式**：`MainWindow.xaml` 设置无边框、顶端无标题栏、无系统标题按钮，`WindowChrome` 保留 8px 原生缩放边框。

```xml
<Window x:Class="WBImageViewer.MainWindow"
        Title="怪叔叔看图"
        Width="760" Height="560"
        MinWidth="140" MinHeight="100"
        WindowStyle="None"
        AllowsTransparency="True"
        ResizeMode="CanResize"
        AllowDrop="True"
        Topmost="True"
        ShowInTaskbar="True"
        Background="Transparent"
        Foreground="#FFE8EAED"
        UseLayoutRounding="True">

    <WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0"
                            ResizeBorderThickness="8"
                            GlassFrameThickness="0"
                            UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
```

**还原要点**：不要回退到自定义 `WM_NCHITTEST`+手工非客户区绘制方案；它曾导致顶部白边。当前依赖 `WindowChrome` 让 Windows 自己处理四边四角缩放。

---

### R02 无多余界面按钮，极简展示图片

**需求原文摘要**：没有多余的界面按钮，以最简洁的方式直接展示图片内容。

**实现状态**：已实现。图片区域始终无按钮；所有操作入口收敛到顶部刘海与右上角悬停关闭区。

**实现逻辑**：主内容区只有 `Image` 与提示文字；控制条默认折叠为一条白色细线，关闭按钮默认在窗口外上方不可见，只有鼠标进入右上角热区时才滑出。

**实现方式**：`MainWindow.xaml` 中 `Root` 只放 `ImageView`、`Hint`、刘海热区、调整抽屉、关闭热区。

```xml
<Grid x:Name="Root" Background="#00000000" ClipToBounds="True" AllowDrop="True" Cursor="Arrow">
    <Canvas x:Name="ImageViewport" Background="Transparent" ClipToBounds="True">
        <Image x:Name="ImageView"
               Stretch="Fill"
               Cursor="Arrow"
               RenderOptions.BitmapScalingMode="HighQuality" />
    </Canvas>

    <TextBlock x:Name="Hint"
               Text="拖入图片，或在资源管理器中双击图片文件"
               FontSize="14"
               Foreground="#FF7A8290"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               IsHitTestVisible="False" />
```

**注意**：后续若新增按钮，必须放入刘海或设置窗口，禁止直接堆到图片区域。

---

### R03 图片置顶与全局快捷键

**需求原文摘要**：图片必须置顶显示，默认 `Shift+Ctrl+Alt+T`，可设置。

**实现状态**：已实现。

**实现逻辑**：
1. 面板 XAML 默认 `Topmost=True`。
2. `HotkeyHost` 在 message-only HWND 上调用 `RegisterHotKey`，快捷键不依赖哪个窗口有焦点。
3. 收到 `WM_HOTKEY` 后分发到当前活动面板。
4. `ToggleTopMost` 仅切换 `Topmost` 并更新刘海置顶按钮图标。

**实现方式**：`Services/HotkeyHost.cs`、`App.xaml.cs`、`MainWindow.xaml.cs`。

```csharp
// MainWindow.xaml.cs
public void ExecuteHotkey(HotkeyAction action)
{
    switch (action)
    {
        case HotkeyAction.ToggleTopMost:
            Topmost = !Topmost;
            PinButton.Content = Topmost ? "⌃" : "⌄";
            break;
        case HotkeyAction.FitWindowToImage:
            FitWindowToImage();
            break;
        case HotkeyAction.FitImageToWindow:
            FitImageToWindow();
            break;
    }
}
```

```csharp
// Services/HotkeyHost.cs
public void Reconfigure(AppSettings settings)
{
    foreach (var id in _registered.Keys.ToArray())
        Win32.UnregisterHotKey(_source.Handle, id);
    _registered.Clear();

    Register(1, HotkeyAction.ToggleTopMost, settings.ToggleTopMostHotkey);
    Register(2, HotkeyAction.FitWindowToImage, settings.FitWindowToImageHotkey);
    Register(3, HotkeyAction.FitImageToWindow, settings.FitImageToWindowHotkey);
}

private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    if (message == Win32.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var action))
    {
        handled = true;
        Pressed?.Invoke(action);
    }
    return IntPtr.Zero;
}
```

```csharp
// App.xaml.cs
private static void OnGlobalHotkey(HotkeyAction action)
{
    var target = Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                 ?? Current.MainWindow as MainWindow
                 ?? Current.Windows.OfType<MainWindow>().FirstOrDefault();
    target?.ExecuteHotkey(action);
}
```

---

### R04 滚轮以鼠标为中心缩放图片，窗口大小不变

**需求原文摘要**：鼠标悬停图片上滚动滚轮放大/缩小图片，窗体大小不变；后续反馈要求必须以鼠标位置为中心缩放。

**实现状态**：已实现，包含锚点修正。

**实现逻辑**：
1. 图片放在专用 `ImageViewport` Canvas 中，画布 `ClipToBounds=True` 且始终等于窗口客户区；图片作为 Canvas 子元素，无论放多大都不会撑大窗口布局。
2. 唯一几何状态是 `_zoom`、`_offsetX`、`_offsetY`；不再使用 `ImageTranslate`/`TranslateTransform`。
3. 滚轮时以视口坐标计算鼠标在“旧图片坐标系”中的相对位置。
4. 用同一相对位置乘以新尺寸，反算新的偏移量，让鼠标下的像素保持不动（锚点不漂移）。
5. `ApplyImagePosition` 按当前视口尺寸重新居中并叠加偏移；窗口尺寸变化也会重新调用，保证画布视线范围始终跟随窗口。
6. 只有按住配置的修饰键时才同步缩放窗口（见 R14）。

**实现方式**：`MainWindow.xaml`、`MainWindow.xaml.cs`。

```csharp
protected override void OnMouseWheel(MouseWheelEventArgs e)
{
    base.OnMouseWheel(e);
    if (_sourcePixels == null) return;
    e.Handled = true;

    var factor = e.Delta > 0 ? App.Settings.ZoomStep : 1.0 / App.Settings.ZoomStep;
    ZoomAt(e.GetPosition(ImageViewport), factor);

    if (HotkeySpec.IsModifierDown(App.Settings.WheelWindowModifiers))
        ZoomWindow(factor);
}

private void ZoomAt(Point point, double factor)
{
    var oldZoom = _zoom;
    var newZoom = Math.Clamp(oldZoom * factor, 0.02, 40);
    if (Math.Abs(newZoom - oldZoom) < 1e-9) return;

    var oldWidth = _pixelWidth * oldZoom;
    var oldHeight = _pixelHeight * oldZoom;
    var oldLeft = (ImageViewport.ActualWidth - oldWidth) / 2.0 + _offsetX;
    var oldTop = (ImageViewport.ActualHeight - oldHeight) / 2.0 + _offsetY;

    var relativeX = (point.X - oldLeft) / oldWidth;
    var relativeY = (point.Y - oldTop) / oldHeight;

    var newWidth = _pixelWidth * newZoom;
    var newHeight = _pixelHeight * newZoom;
    var newLeft = (ImageViewport.ActualWidth - newWidth) / 2.0;
    var newTop = (ImageViewport.ActualHeight - newHeight) / 2.0;

    _offsetX = point.X - newLeft - relativeX * newWidth;
    _offsetY = point.Y - newTop - relativeY * newHeight;
    _zoom = newZoom;
    ApplyImageSize();
}

private void ApplyImageSize()
{
    if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
    ImageView.Width = _pixelWidth * _zoom;
    ImageView.Height = _pixelHeight * _zoom;
    ApplyImagePosition();
}

private void ApplyImagePosition()
{
    if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
    var centerX = (ImageViewport.ActualWidth - ImageView.Width) / 2.0;
    var centerY = (ImageViewport.ActualHeight - ImageView.Height) / 2.0;
    Canvas.SetLeft(ImageView, centerX + _offsetX);
    Canvas.SetTop(ImageView, centerY + _offsetY);
}
```

**历史错误与禁止回退**：曾使用 `ScaleTransform`+`RenderTransformOrigin` 导致缩放中心漂移；后又出现 `TranslateTransform` 与过大 `Image` 布局互相干扰、窗口被无形边界切割的错乱。禁止恢复 `ImageTranslate`/中心变换方案，必须保留 `ImageViewport` 画布裁切。

---

### R05 窗口适配图片

**需求原文摘要**：`Shift+Ctrl+Alt+R`，让窗口按当前图片大小调整，刚好容纳图片，且不超过桌面最大窗口范围。

**实现状态**：已实现；R47 修正了画布留白问题，当前代码以本条末尾片段为准。

**实现逻辑**：`WindowChrome` 的外框包含不可见的 8px 缩放边框，若直接把图片显示尺寸当作窗口外框尺寸，客户区会小于图片而出现留白/裁切。当前先测出外框与客户区差值，目标外框 = 图片显示尺寸 + 该差值；超工作区时等比缩小并按最终客户区重算 `_zoom`；`SetWindowPos` 后在 `Dispatcher.Render` 回调中按最终视口尺寸再次锁定 `_zoom`，使四边完全重合。

**实现方式**：`MainWindow.xaml.cs`。

```csharp
// 完整实现见 R47。核心：外框 = 图片显示尺寸 + 隐形缩放边框；布局后回调锁 _zoom。
private void FitWindowToImage()
{
    // 1. 边框差值
    var borderX = rect.Width - (int)Math.Round(ImageViewport.ActualWidth * dpi.DpiScaleX);
    var borderY = rect.Height - (int)Math.Round(ImageViewport.ActualHeight * dpi.DpiScaleY);
    // 2. 目标客户区 = 图片当前显示尺寸
    var clientW = Math.Max(1, (int)Math.Ceiling(_pixelWidth * _zoom * dpi.DpiScaleX));
    var clientH = Math.Max(1, (int)Math.Ceiling(_pixelHeight * _zoom * dpi.DpiScaleY));
    var width = clientW + Math.Max(0, borderX);
    var height = clientH + Math.Max(0, borderY);
    // 3. 工作区限制后 SetWindowPos，并在 Render 回调中按最终视口锁定 _zoom。
}
```

---

### R06 图片适配窗口

**需求原文摘要**：`Shift+Ctrl+Alt+S`，图片按当前窗口大小平铺，按长宽最大值匹配，等比缩放不拉伸。

**实现状态**：已实现。

**实现逻辑**：取窗口宽高与图片宽高的较小比例作为 `_zoom`，保证图片完整落在窗口内，比例不变形，并把偏移归零。

**实现方式**：`MainWindow.xaml.cs`。

```csharp
/// <summary>Fit the image inside the current window, preserving aspect ratio (no stretching).</summary>
public void FitImageToWindow()
{
    if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
    var width = ImageViewport.ActualWidth;
    var height = ImageViewport.ActualHeight;
    if (width < 8 || height < 8) return;

    _zoom = Math.Min(width / _pixelWidth, height / _pixelHeight);
    _offsetX = _offsetY = 0;
    ApplyImageSize();
}
```

---

### R07 系统默认图片浏览器与双击打开

**需求原文摘要**：软件注入系统，资源管理器双击图片自动用本软件打开。

**实现状态**：已实现为“注册候选 + 打开系统默认应用确认”。Windows 11 的 `UserChoice`/`UserChoiceLatest` 受保护，程序不能可靠静默改写，且不应伪造。

**实现逻辑**：
1. 在 `HKCU\Software\Classes\GuaishushuViewer.Image` 注册 ProgID。
2. 写入 `DefaultIcon` 与 `shell\open\command`。
3. 对支持的扩展名写 `OpenWithProgids`，并备份原关联到 `PreviousAssociations`。
4. 调用 `SHChangeNotify` 刷新 Shell。
5. 通过 `Process.Start(..., "openas")` 打开系统“打开方式”对话框，由用户一次性确认；失败时打开 `ms-settings:defaultapps`。
6. 取消关联时只恢复本软件写过的项，不误伤其他关联。

**实现方式**：`Services/FileAssociationService.cs`。

```csharp
private const string ProgId = "GuaishushuViewer.Image";

private static readonly string[] Extensions =
    { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico" };

public static void Apply(AppSettings settings)
{
    if (settings.AssociateAsDefaultViewer) Register(settings);
    else Revert(settings);
}

private static void Register(AppSettings settings)
{
    var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GuaishushuViewer.exe");

    using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
    {
        progId.SetValue(string.Empty, "怪叔叔看图");
        using var icon = progId.CreateSubKey("DefaultIcon");
        icon.SetValue(string.Empty, $"\"{exe},0\"");
        using var command = progId.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
    }

    foreach (var extension in Extensions)
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
        {
            if (!settings.PreviousAssociations.ContainsKey(extension))
                settings.PreviousAssociations[extension] = key.GetValue(string.Empty) as string;
            key.SetValue(string.Empty, ProgId);
        }

        using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
        openWith.SetValue(ProgId, string.Empty, RegistryValueKind.String);
    }

    SettingsService.Save(settings);
    NotifyShell();
}

public static void OpenChooseAppDialog()
{
    try
    {
        var sample = Path.Combine(Path.GetTempPath(), "guaishushu-viewer-sample.png");
        File.WriteAllBytes(sample, Convert.FromBase64String(SamplePng));
        Process.Start(new ProcessStartInfo(sample) { Verb = "openas", UseShellExecute = true });
        return;
    }
    catch { }

    try
    {
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }
    catch { }
}
```

**历史结论**：不要尝试伪造 `UserChoice` Hash；实测 Win11 会回滚。不要无条件删除 `UserChoice`，只能通过系统对话框让用户确认。

---

### R08 单窗口/多窗口运行逻辑

**需求原文摘要**：后台可设置每次打开图片在同一窗口还是新窗口，默认单窗口。

**实现状态**：已实现。

**实现逻辑**：
- 单窗口：启动参数中的图片传给已有实例；已有实例复用第一个 `MainWindow` 并 `LoadImage`。
- 多窗口：每个图片文件创建一个新 `MainWindow`。
- 无图片参数时至少打开一个空面板。

**实现方式**：`App.xaml.cs` 与 `Services/ImagePipe.cs`。

```csharp
// App.xaml.cs
var files = e.Args.Where(ImageFile.IsSupported).ToArray();

if (Settings.SingleWindow && ImagePipe.TrySend(files))
{
    Shutdown();
    return;
}

if (Settings.SingleWindow)
    ImagePipe.Start(OpenFiles);

SetupTray();
OpenFiles(files);

if (Current.Windows.Count == 0)
    OpenFiles(Array.Empty<string>());
```

```csharp
private void OpenFiles(IReadOnlyList<string> files)
{
    if (Settings.SingleWindow)
    {
        var existing = Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (existing != null)
        {
            if (files.Count > 0) existing.LoadImage(files[0]);
            existing.Show();
            existing.Activate();
            return;
        }

        var window = new MainWindow();
        Current.MainWindow = window;
        if (files.Count > 0) window.LoadImage(files[0]);
        window.Show();
        return;
    }

    if (files.Count == 0)
    {
        var window = new MainWindow();
        window.Show();
        Current.MainWindow ??= window;
        return;
    }

    foreach (var file in files)
    {
        var window = new MainWindow();
        window.LoadImage(file);
        window.Show();
        Current.MainWindow ??= window;
    }
}
```

```csharp
// Services/ImagePipe.cs
private const string PipeName = "guaishushu-viewer-v1";

public static bool TrySend(IReadOnlyList<string> files)
{
    try
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        client.Connect(300);
        client.ReadTimeout = 800;
        client.WriteTimeout = 800;
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        writer.WriteLine(string.Join('\n', files));
        return string.Equals(reader.ReadLine(), "OK", StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}
```

---

### R09 四边四角原生窗口缩放

**需求原文摘要**：窗口四边与四角触发调节大小，完全采用 Windows 逻辑。

**实现状态**：已实现。

**实现逻辑**：使用 `WindowChrome.ResizeBorderThickness=8` 让 Windows 原生处理 `WM_NCHITTEST` 八方向缩放；`WM_GETMINMAXINFO` 限制最小尺寸与最大化范围在工作区内。

**实现方式**：`MainWindow.xaml` + `MainWindow.xaml.cs` + `Native/Win32.cs`。

```xml
<shell:WindowChrome CaptionHeight="0"
                    ResizeBorderThickness="8"
                    GlassFrameThickness="0"
                    UseAeroCaptionButtons="False" />
```

```csharp
private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    if (message == Win32.WM_GETMINMAXINFO)
    {
        var area = WorkAreaPhysical();
        var dpi = VisualTreeHelper.GetDpi(this);
        var info = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
        info.ptMinTrackSize = new Win32.POINT(
            (int)Math.Round(140 * dpi.DpiScaleX), (int)Math.Round(100 * dpi.DpiScaleY));
        info.ptMaxSize = new Win32.POINT(area.Width, area.Height);
        info.ptMaxPosition = new Win32.POINT(area.Left, area.Top);
        info.ptMaxTrackSize = new Win32.POINT(area.Width, area.Height);
        Marshal.StructureToPtr(info, lParam, true);
        handled = true;
    }
    return IntPtr.Zero;
}
```

**禁止回退**：不要再写 `WM_NCHITTEST` 返回 `HTLEFT/HTTOP...` 的手工逻辑；它与 `WindowChrome` 的非客户区绘制冲突，曾造成顶部白边。

---

### R10 可配置窗口移动方式，默认右键

**需求原文摘要**：悬停移动窗口，预设滚轮/中键、右键、空格+左键，默认右键，后台可设置。

**实现状态**：已实现。

**实现逻辑**：`PreviewMouseDown` 判断按键是否符合 `MoveMode`，且不在四边四角缩放热区时，记录光标与窗口矩形，捕获鼠标，随后 `PreviewMouseMove` 用 `SetWindowPos` 实时移动窗口。

**实现方式**：`MainWindow.xaml.cs`、`Models/AppSettings.cs`。

```csharp
var mode = App.Settings.MoveMode;
var wantsMove = e.ChangedButton switch
{
    MouseButton.Right => mode == WindowMoveMode.RightButton,
    MouseButton.Middle => mode == WindowMoveMode.MiddleButton,
    MouseButton.Left => mode == WindowMoveMode.SpaceAndLeftButton && Win32.IsKeyDown(Win32.VK_SPACE),
    _ => false
};

if (wantsMove && !IsOverResizeBorder())
{
    BeginWindowMove();
    e.Handled = true;
    return;
}
```

```csharp
private void BeginWindowMove()
{
    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var cursor) || !Win32.GetWindowRect(hwnd, out var rect))
        return;

    _moveCursorStart = cursor;
    _moveWindowStart = rect;
    _movingWindow = true;
    SetGrabCursor();
    CaptureMouse();
}

protected override void OnPreviewMouseMove(MouseEventArgs e)
{
    base.OnPreviewMouseMove(e);

    if (_movingWindow)
    {
        if (Win32.GetCursorPos(out var cursor))
        {
            var dx = cursor.X - _moveCursorStart.X;
            var dy = cursor.Y - _moveCursorStart.Y;
            Win32.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                _moveWindowStart.Left + dx, _moveWindowStart.Top + dy, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
        e.Handled = true;
        return;
    }
```

---

### R11 最低硬件资源渲染

**需求原文摘要**：必须占用最小硬件资源渲染图片。

**实现状态**：已实现。

**实现逻辑**：
1. 图片像素只在打开图片或调整参数变化时处理。
2. 处理完成后写入 `WriteableBitmap`，之后由 DWM 合成静态纹理。
3. 没有 `CompositionTarget.Rendering` 或无限动画循环。
4. 图片增强算法只用整数数组，无第三方图像库。
5. 刘海与关闭按钮动画使用离散的短时 transform/clip 动画，不持续运行。

**实现方式**：`MainWindow.xaml.cs`、`Services/ImageEnhanceService.cs`。

```csharp
private void ApplyAdjustments()
{
    if (_sourcePixels == null || _displayPixels == null || _display == null) return;
    var pixels = _displayPixels;
    ImageEnhanceService.Apply(_sourcePixels, pixels, _pixelWidth, _pixelHeight, _adjustments);
    _display.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight),
        pixels, _pixelWidth * 4, 0);
}
```

```csharp
var bitmap = new BitmapImage();
bitmap.BeginInit();
bitmap.CacheOption = BitmapCacheOption.OnLoad;
bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
bitmap.UriSource = new Uri(path, UriKind.Absolute);
bitmap.EndInit();
bitmap.Freeze();
```

---

### R12 窗口不活动时尽量释放资源但仍置顶

**需求原文摘要**：窗口不活动时，在保证正常置顶显示图片的前提下，尽量释放硬件资源。

**实现状态**：已实现。当前实现不卸载图片（否则无法继续显示），但把插值质量降到低档，减少非活动窗口的采样成本；图片仍由 DWM 静态合成并保持置顶。

**实现方式**：`MainWindow.xaml.cs`。

```csharp
protected override void OnDeactivated(EventArgs e)
{
    base.OnDeactivated(e);
    RenderOptions.SetBitmapScalingMode(ImageView, BitmapScalingMode.LowQuality);
}

protected override void OnActivated(EventArgs e)
{
    base.OnActivated(e);
    RenderOptions.SetBitmapScalingMode(ImageView, BitmapScalingMode.HighQuality);
}
```

---

### R13 Windows 最合适技术栈

**需求原文摘要**：必须以最适合 Windows 平台的技术栈开发。

**实现状态**：已实现。

**实现逻辑**：选择 WPF/.NET 10：
- DirectX/DWM 合成，适合无边框半透明置顶面板；
- 原生 HWND 便于 `RegisterHotKey`、`SetWindowPos`、`WindowChrome`；
- 单项框架依赖，单文件发布体积较小；
- WinForms 仅作为系统托盘、屏幕工作区、颜色对话框的轻量补充。

**实现方式**：`.csproj`。

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
```

---

### R14 修饰键 + 滚轮同步缩放窗口

**需求原文摘要**：配合窗口自动调整功能，按住快捷键配合滚轮，放大图片的同时放大窗口。

**实现状态**：已实现，默认 `Ctrl`，可在设置中改。

**实现逻辑**：滚轮先缩放图片；若 `WheelWindowModifiers` 全部按下，再以窗口中心为锚点按相同比例缩放窗口，并限制在工作区内。

**实现方式**：`MainWindow.xaml.cs`、`HotkeySpec.IsModifierDown`。

```csharp
if (HotkeySpec.IsModifierDown(App.Settings.WheelWindowModifiers))
    ZoomWindow(factor);
```

```csharp
private void ZoomWindow(double factor)
{
    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return;

    var area = WorkAreaPhysical();
    var centerX = (rect.Left + rect.Right) / 2;
    var centerY = (rect.Top + rect.Bottom) / 2;
    var width = Math.Clamp((int)Math.Round(rect.Width * factor), 140, area.Width);
    var height = Math.Clamp((int)Math.Round(rect.Height * factor), 100, area.Height);
    var left = Math.Clamp(centerX - width / 2, area.Left, Math.Max(area.Left, area.Right - width));
    var top = Math.Clamp(centerY - height / 2, area.Top, Math.Max(area.Top, area.Bottom - height));
    Win32.SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height,
        Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
}
```

---

### R15 左键拖动平移图片

**需求原文摘要**：鼠标悬停图片上按住左键可平移图片，用于放大后查看局部。

**实现状态**：已实现。

**实现逻辑**：`PreviewMouseDown` 左键且已有图片时进入 `_panningImage`，捕获鼠标并切换系统手型光标；`PreviewMouseMove` 把位移累加到 `_offsetX/_offsetY`，并通过 `ApplyImagePosition()` 在视口内重新定位；松开或丢失捕获后恢复箭头并释放捕获。

**实现方式**：`MainWindow.xaml.cs`。

```csharp
if (e.ChangedButton == MouseButton.Left && _sourcePixels != null)
{
    _panningImage = true;
    _lastPanPosition = e.GetPosition(ImageViewport);
    SetGrabCursor();
    CaptureMouse();
    e.Handled = true;
}
```

```csharp
if (_panningImage)
{
    var current = e.GetPosition(ImageViewport);
    _offsetX += current.X - _lastPanPosition.X;
    _offsetY += current.Y - _lastPanPosition.Y;
    _lastPanPosition = current;
    ApplyImagePosition();
    e.Handled = true;
}
```

```csharp
protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
{
    base.OnPreviewMouseUp(e);
    if (_movingWindow || _panningImage)
    {
        _movingWindow = false;
        _panningImage = false;
        RestoreCursor();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }
}

protected override void OnLostMouseCapture(MouseEventArgs e)
{
    base.OnLostMouseCapture(e);
    _movingWindow = false;
    _panningImage = false;
    RestoreCursor();
}
```

---

## 3. 系统集成、拖拽与窗口装饰问题修复

### R16 默认关联失效修复

**需求原文摘要**：软件运行但没有注入系统，双击图片没有用本软件打开。

**实现状态**：已实现为注册候选 + 系统确认。

**实现逻辑**：见 R07。另外启动时、设置保存时、安装结束时都会调用 `FileAssociationService.Apply`。`--set-default` 参数专门用于安装后打开“打开方式”。

**实现方式**：`App.xaml.cs`。

```csharp
if (e.Args.Any(arg => arg is "--set-default" or "--setdefault"))
{
    try { FileAssociationService.Apply(Settings); } catch { }
    FileAssociationService.OpenChooseAppDialog();
    Shutdown();
    return;
}

try
{
    FileAssociationService.Apply(Settings);
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Association failed: {ex.Message}");
}
```

```csharp
public static void ApplySettings(AppSettings settings)
{
    Settings = settings;
    try
    {
        FileAssociationService.Apply(settings);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"设置已保存，但文件关联修改失败：{ex.Message}", AppInfo.Name,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    SettingsService.Save(settings);
    Hotkeys.Reconfigure(settings);
    foreach (var window in Current.Windows.OfType<MainWindow>())
    {
        window.ApplyVisualSettings(settings);
        window.ApplySavedAdjustments();
    }
}
```

**历史处置**：曾测试 Windows 11 `UserChoiceLatest\ProgId` 与 Hash 写入，确认系统受保护且会回滚；测试期间修改的注册表关联已恢复。

---

### R17 拖入图片失效修复

**需求原文摘要**：拖动 jpg/png/bmp 到窗口无效。

**实现状态**：已实现。

**实现逻辑**：根因是未设置 `AllowDrop`。现在 Window 与 Root 都允许拖放，并且 `DragEnter`/`DragOver` 明确设置 `DragDropEffects.Copy`，`Drop` 取第一个受支持图片路径加载。

**实现方式**：`MainWindow.xaml(.cs)`、`Services/SettingsService.cs` 中的 `ImageFile.IsSupported`。

```xml
<Window AllowDrop="True">
...
<Grid x:Name="Root" AllowDrop="True">
```

```csharp
Drop += OnDrop;
DragOver += (_, e) =>
{
    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
};
DragEnter += (_, e) =>
{
    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
};
```

```csharp
private void OnDrop(object sender, DragEventArgs e)
{
    if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
    if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
    var image = files.FirstOrDefault(ImageFile.IsSupported);
    if (image != null) LoadImage(image);
}
```

```csharp
internal static class ImageFile
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"
    };

    public static bool IsSupported(string path)
    {
        try
        {
            return File.Exists(path) && Extensions.Contains(Path.GetExtension(path));
        }
        catch
        {
            return false;
        }
    }
}
```

---

### R18 顶部白边移除

**需求原文摘要**：窗口顶部有多余白边，不需要任何窗口元素。

**实现状态**：已实现。

**实现逻辑**：移除自定义非客户区钩子，改用 `WindowChrome` 的 `CaptionHeight=0`、`GlassFrameThickness=0`、`UseAeroCaptionButtons=False`。顶层 Grid 自己绘制背景并 `ClipToBounds=True`。

**实现方式**：`MainWindow.xaml`。

```xml
<shell:WindowChrome CaptionHeight="0"
                    ResizeBorderThickness="8"
                    GlassFrameThickness="0"
                    UseAeroCaptionButtons="False" />

<Grid x:Name="Root" Background="#00000000" ClipToBounds="True" AllowDrop="True" Cursor="Arrow">
```

**禁止回退**：不要恢复 `WM_NCHITTEST` 手工命中测试与非客户区擦除方案。

---

### R19 圆角过大修复与后台设置

**需求原文摘要**：四角圆角太大，减小，并在后台提供圆角设置功能。

**实现状态**：已实现。默认 4，范围 0-24。

**实现逻辑**：后台输入整数；保存时校验；`ApplyVisualSettings` 应用到所有面板；窗口尺寸变化时按 DWM 档位重算。当前不使用 GDI region，直接设置 DWM 窗口圆角偏好（见 R26）。

**实现方式**：`SettingsWindow.xaml`、`SettingsWindow.xaml.cs`、`MainWindow.xaml.cs`。

```xml
<TextBlock Text="面板圆角半径（0 - 24，0 为直角，由 DWM 绘制抗锯齿边缘）"
           FontWeight="SemiBold" Margin="0,0,0,6" />
<TextBox x:Name="CornerRadiusBox" Margin="0,0,0,14" />
```

```csharp
if (!int.TryParse(CornerRadiusBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var radius)
    || radius < 0 || radius > 24)
{
    MessageBox.Show("圆角半径请输入 0 到 24 之间的整数。", "设置无效",
        MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
}
```

```csharp
SizeChanged += (_, _) => ApplyCornerRadius(App.Settings.CornerRadius);
```

---

### R20 图标与托盘 logo 修复

**需求原文摘要**：软件图标太丑，托盘图标没有显示 logo。

**实现状态**：已实现。多尺寸 ICO 内嵌为资源，托盘优先从资源加载，失败回退 exe 图标。

**实现方式**：`Assets/app.ico`、`.csproj`、`App.xaml.cs`。

```xml
<ApplicationIcon>Assets\app.ico</ApplicationIcon>
<ItemGroup>
  <Resource Include="Assets\app.ico" />
  <Resource Include="Assets\donate.png" />
</ItemGroup>
```

```csharp
private static System.Drawing.Icon LoadTrayIcon()
{
    try
    {
        var resource = GetResourceStream(new Uri("Assets/app.ico", UriKind.Relative));
        if (resource?.Stream != null)
            return new System.Drawing.Icon(resource.Stream);
    }
    catch
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path != null) return System.Drawing.Icon.ExtractAssociatedIcon(path) ?? System.Drawing.SystemIcons.Application;
        }
        catch { }
    }
    return System.Drawing.SystemIcons.Application;
}
```

---

### R25 右上角悬停展开关闭按钮

**需求原文摘要**：右上角常规窗口置顶/关闭按钮位置绘制按钮区，悬停后向下抽出；后续要求置顶按钮改为图钉并纳入同一伸缩区（见 R43）。

**实现状态**：已实现。

**实现逻辑**：透明 `CloseHost` 宽 64 高 40 贴右上，内部的 `CloseStrip`（图钉 + 关闭）整体初始 `TranslateTransform.Y=-32`，`MouseEnter` 动画到 2，`MouseLeave` 回到 -32。两个按钮共享同一个变换，保证同步伸出与收回。

**实现方式**：`MainWindow.xaml(.cs)`。

```xml
<Grid x:Name="CloseHost" Width="64" Height="40" HorizontalAlignment="Right"
      VerticalAlignment="Top" Background="Transparent" ClipToBounds="True">
    <StackPanel x:Name="CloseStrip" Orientation="Horizontal" HorizontalAlignment="Right"
                VerticalAlignment="Top">
        <StackPanel.RenderTransform>
            <TranslateTransform x:Name="CloseTransform" Y="-32" />
        </StackPanel.RenderTransform>

        <Button x:Name="PinButton" Width="30" Height="28" Margin="0,2,2,0"
                Focusable="False" Cursor="Hand" Click="PinButton_Click"
                ToolTip="切换置顶 (Shift+Ctrl+Alt+T)">
            <Button.Template>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" Background="#CC242830" CornerRadius="6,0,0,6">
                        <Path Width="13" Height="13" Stretch="Uniform" Fill="#FFD7DEE8"
                              Data="M6.5,1.5 H9.5 V2.5 H10.5 C11.1,2.5 11.5,2.9 11.5,3.5 V4.5 C11.5,5.1 11.1,5.5 10.5,5.5 H9.8 L9,10.5 L12,14 H4 L7,10.5 L6.2,5.5 H5.5 C4.9,5.5 4.5,5.1 4.5,4.5 V3.5 C4.5,2.9 4.9,2.5 5.5,2.5 H6.5 Z" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#FF3A4250" />
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#FF4C5668" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Button.Template>
        </Button>

        <Button x:Name="CloseButton" Width="28" Height="28" Margin="0,2,0,0"
                Content="×" FontSize="17" Foreground="#FFE8EAED"
                Focusable="False" Cursor="Hand" Click="CloseButton_Click"
                ToolTip="关闭窗口">
            <Button.Template>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" Background="#CCB42B2B" CornerRadius="0,0,6,6">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#FFE04B4B" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Button.Template>
        </Button>
    </StackPanel>
</Grid>
```

```csharp
CloseHost.MouseEnter += (_, _) => AnimateCloseButton(2);
CloseHost.MouseLeave += (_, _) => AnimateCloseButton(-32);

private void AnimateCloseButton(double targetY)
{
    var animation = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(180))
    {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };
    CloseTransform.BeginAnimation(TranslateTransform.YProperty, animation);
}

private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
```

---

### R26 边缘锯齿优化

**需求原文摘要**：窗口边缘锯齿感严重，优化。

**实现状态**：已实现。

**实现逻辑**：删除 `SetWindowRgn/CreateRoundRectRgn` 的 GDI 硬裁剪，改用 `DWMWA_WINDOW_CORNER_PREFERENCE`（attribute 33）让 DWM 绘制抗锯齿圆角：
- `radius <= 0` → preference 1（直角）
- `1..12` → preference 3（小圆角）
- `>12` → preference 2（标准圆角）

**实现方式**：`MainWindow.xaml.cs`、`Native/Win32.cs`。

```csharp
/// <summary>DWM draws anti-aliased corners, avoiding the jagged GDI window region.</summary>
public void ApplyCornerRadius(int radius)
{
    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero) return;

    // Remove any legacy GDI region first - it is the source of hard, pixelated edges.
    Win32.SetWindowRgn(hwnd, IntPtr.Zero, true);

    var preference = radius <= 0 ? 1 : radius <= 12 ? 3 : 2;
    Win32.DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
}
```

```csharp
[DllImport("gdi32.dll")]
public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

[DllImport("dwmapi.dll")]
public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
```

> `CreateRoundRectRgn` 声明仅为兼容/清理旧 region 保留；正常路径不创建新 region。

---

## 4. 发布、GitHub 与项目治理

### R23 单文件运行包 bat

**需求原文摘要**：根目录做批处理，封装单文件运行包。

**实现状态**：已实现。

**实现逻辑**：检查 `dotnet`，清理 `dist/single`，从 `.csproj` 的 `<Version>` 解析版本号，执行 framework-dependent single-file publish，同时生成带版本号的 exe 与固定名称兼容 exe。`--ci` 参数跳过 `pause`。

**实现方式**：`封装单文件运行包.bat`。

```bat
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "OUT=%ROOT%dist\single"
set "VERSION="
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION set "VERSION=1.2.0"
set "VERSIONED_EXE=%ROOT%GuaishushuViewer-v%VERSION%.exe"

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%OUT%"

copy /Y "%OUT%\GuaishushuViewer.exe" "%ROOT%GuaishushuViewer.exe" >nul
copy /Y "%OUT%\GuaishushuViewer.exe" "%VERSIONED_EXE%" >nul

:end
if /i not "%~1"=="--ci" pause
```

**注意**：bat 使用 GBK 编码，避免中文路径与中文输出在 cmd 中乱码。

---

### R24 安装包 bat 与安装脚本

**需求原文摘要**：根目录做安装包 bat；安装后注册系统并尽量设为默认看图。

**实现状态**：已实现。IExpress 在中文路径/环境不可靠，因此采用 ZIP + `install.cmd`。

**实现方式**：`封装安装包.bat` 从 `.csproj` 读取版本号，发布程序、复制 `install.cmd`、压缩为 `dist/GuaishushuViewer-v<版本>-Setup.zip`（同时保留固定名称兼容副本）；`packaging/install.cmd` 复制到 `%LOCALAPPDATA%\GuaishushuViewer`，写注册表，创建快捷方式，添加卸载项，最后运行 `--set-default`。

```bat
set "STAGE=%TEMP%\GuaishushuViewerSetup"
set "PACKAGE=%STAGE%\GuaishushuViewer-Setup.zip"
set "VERSION="
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION set "VERSION=1.2.0"
set "FINAL=%ROOT%dist\GuaishushuViewer-v%VERSION%-Setup.zip"
set "STABLE=%ROOT%dist\GuaishushuViewer-Setup.zip"

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
copy /Y "%ROOT%packaging\install.cmd" "%STAGE%\install.cmd" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%PACKAGE%' -Force"
copy /Y "%PACKAGE%" "%FINAL%" >nul
copy /Y "%PACKAGE%" "%STABLE%" >nul
```

```bat
set "APP=GuaishushuViewer.exe"
set "DEST=%LOCALAPPDATA%\GuaishushuViewer"
set "PROGID=GuaishushuViewer.Image"

if not exist "%DEST%" mkdir "%DEST%" >nul
copy /Y "%~dp0%APP%" "%DEST%\%APP%" >nul

reg add "HKCU\Software\Classes\%PROGID%" /ve /d "怪叔叔看图" /f >nul
reg add "HKCU\Software\Classes\%PROGID%\DefaultIcon" /ve /d "\"%DEST%\%APP%,0\"" /f >nul
reg add "HKCU\Software\Classes\%PROGID%\shell\open\command" /ve /d "\"%DEST%\%APP%\" \"%%1\"" /f >nul

for %%E in (.png .jpg .jpeg .jfif .bmp .gif .webp .tif .tiff .ico) do (
  reg add "HKCU\Software\Classes\%%E" /ve /d "%PROGID%" /f >nul
  reg add "HKCU\Software\Classes\%%E\OpenWithProgids" /v "%PROGID%" /d "" /f >nul
)

start "" "%DEST%\%APP%" --set-default
```

---

### R27 GitHub 建仓、项目管理与下载

**需求原文摘要**：在用户 GitHub 建立项目、上传、配置项目管理功能、提供单文件与安装包下载。

**实现状态**：已实现。

**实现内容**：
- 仓库：`https://github.com/yysdgss/guaishushu-image-viewer`
- Release：`v1.2.0`
- Release 资产：
  - `GuaishushuViewer-1.2.0-win-x64.exe`
  - `GuaishushuViewer-1.2.0-Setup.zip`
- Issue 模板：`.github/ISSUE_TEMPLATE/bug_report.md`、`feature_request.md`
- 标签与里程碑已配置。
- 本地 Git HTTPS 推送受 TLS/低速限制阻断时，改用 GitHub API 依次创建 blob → tree → commit → ref，再上传 Release 资产。

**还原步骤**：若需重建远端，先配置 Git 凭据；失败时使用 GitHub Contents API 上传当前仓库全量文件，再用 Releases API 上传两个资产。

> 当前源码在 v1.2.0 发布后又修改了刘海轮廓、光标行为与带版本号封装命名；Remote Release 资产尚未同步这些最新改动。重新分发前必须重新打包并上传新资产，或升版为 `1.2.1`。

---

### R28 更名

**需求原文摘要**：项目和软件更名为“怪叔叔看图”。用户后续明确指出此前写入的“该怪叔叔看图”是笔误，已在全项目更正为“怪叔叔看图”（见 R41）。

**实现状态**：已实现，名称已更正。

**实现代码**：

```csharp
// Models/AppInfo.cs
public const string Name = "怪叔叔看图";
```

```xml
<!-- WBImageViewer.csproj -->
<AssemblyName>GuaishushuViewer</AssemblyName>
<Product>怪叔叔看图</Product>
<Authors>怪叔叔</Authors>
```

---

### R29 版本号、关于界面与托盘状态

**需求原文摘要**：按常规版本命名规则添加版本号；后台添加关于界面显示版本、功能、操作方法；托盘右键菜单最下方预览状态栏显示软件名和版本。

**实现状态**：已实现。

**实现方式**：`AppInfo.Version`、`.csproj` 三处版本字段、`AboutWindow`、`App.xaml.cs` 托盘菜单。

```csharp
// AboutWindow.xaml.cs
public AboutWindow()
{
    InitializeComponent();
    ProductName.Text = AppInfo.Name;
    VersionText.Text = $"版本 {AppInfo.Version} · MIT 免费开源";
}
```

```csharp
// App.xaml.cs
menu.Items.Add(new ToolStripSeparator());
var status = new ToolStripLabel($"{AppInfo.Name}{Environment.NewLine}v{AppInfo.Version}")
{
    AutoSize = false,
    Size = new System.Drawing.Size(154, 34),
    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
    ForeColor = System.Drawing.Color.FromArgb(0x70, 0x75, 0x80),
    Font = new System.Drawing.Font("Segoe UI", 8)
};
menu.Items.Add(status);

_tray = new NotifyIcon
{
    Icon = LoadTrayIcon(),
    Text = $"{AppInfo.Name} v{AppInfo.Version}",
    ContextMenuStrip = menu,
    Visible = true
};
```

---

### R30 开发档案与开源许可

**需求原文摘要**：建立 md 档案存放开发档案和免费开源许可。

**实现状态**：已实现。开发档案是本文件 `DEVELOPMENT.md`；许可为根目录 `LICENSE`，采用 MIT。

**维护规则**：见本文开头“强制维护规则”。

---

### R31 打赏二维码

**需求原文摘要**：在开源 MD 档案和软件关于界面提供打赏作者二维码。

**实现状态**：已实现。

**实现方式**：`src/WBImageViewer/Assets/donate.png` 内嵌资源；README 与关于窗口显示。

```xml
<Image Source="pack://application:,,,/Assets/donate.png" Width="132" Height="132"
       HorizontalAlignment="Left" RenderOptions.BitmapScalingMode="HighQuality" />
```

```markdown
<img src="src/WBImageViewer/Assets/donate.png" width="220" alt="打赏二维码" />
```

---

## 5. 刘海控制条与视觉细节

### R32 顶部斜刘海控制条

**需求原文摘要**：软件顶部增加斜刘海，类似岛仔项目外形，提供窗口适配图片、图片适配窗口、设置等常规按钮。

**实现状态**：已实现，并已在 R37/R45 按用户截图进一步重建。

**实现逻辑**：顶部中央一个透明热区，内含裁剪后的灵动岛轮廓；上方向两侧外扩且在肩部形成内圆角，下方向内收拢并以圆角收口；包含窗口适配图片、图片适配窗口、时钟、图片调整、设置、关于。置顶按钮已按 R43 移到右上角关闭伸缩区，不再放在岛内。

**实现方式**：`MainWindow.xaml`。

> 以下为 R50 时期的历史片段，仅作演化记录；灵动岛现为 200×34、右肩镜像左肩（R54），且**时钟已于 R72 删除**，当前权威代码以 `MainWindow.xaml` 源文件为准。

```xml
<Grid x:Name="IslandHitArea"
      Width="232" Height="46"
      HorizontalAlignment="Center" VerticalAlignment="Top"
      Background="Transparent" SnapsToDevicePixels="False">
    <Grid x:Name="IslandRoot"
          Width="232" Height="34"
          HorizontalAlignment="Center" VerticalAlignment="Top">
        <Grid.Clip>
            <RectangleGeometry x:Name="IslandClip" Rect="0,0,232,2" />
        </Grid.Clip>

        <Path x:Name="IslandShape" Stretch="Fill"
              Data="M 6,0 H 226 C 228.5,0 230,1.8 230,4 C 230,5.2 226.5,6.2 222,7 V 14 C 222,17 221,19.2 219,22 C 215,29 208,34 196,34 H 36 C 24,34 17,29 13,22 C 11,19.2 10,17 10,14 V 7 C 5.5,6.2 2,5.2 2,4 C 2,1.8 3.5,0 6,0 Z"
              Fill="#F2353537" Stroke="#FFFFFF" StrokeThickness="1">
            <Path.Effect>
                <DropShadowEffect Color="#000000" Opacity="0.38" BlurRadius="8" ShadowDepth="2" />
            </Path.Effect>
        </Path>

        <Rectangle x:Name="IslandHairline" Height="2" Margin="2,0,2,0"
                   VerticalAlignment="Top" RadiusX="1" RadiusY="1" Fill="#FFFFFF" />

        <StackPanel x:Name="IslandButtons"
                    Orientation="Horizontal" HorizontalAlignment="Center"
                    VerticalAlignment="Center" Margin="0,1,0,0"
                    Opacity="0" IsHitTestVisible="False">
            <Button x:Name="WindowToImageButton" Style="{StaticResource IslandButton}" Width="26" Height="26" FontSize="14" Content="⤢"
                    ToolTip="窗口适配图片 (Shift+Ctrl+Alt+R)" Click="WindowToImageButton_Click" />
            <Button x:Name="ImageToWindowButton" Style="{StaticResource IslandButton}" Width="26" Height="26" FontSize="14" Content="⤡"
                    ToolTip="图片适配窗口 (Shift+Ctrl+Alt+S)" Click="ImageToWindowButton_Click" />
            <TextBlock x:Name="IslandClock" Width="40" Text="--:--" FontSize="10"
                       Foreground="#FFD7DEE8" TextAlignment="Center" VerticalAlignment="Center" />
            <Button x:Name="AdjustButton" Style="{StaticResource IslandButton}" Width="26" Height="26" FontSize="14" Content="◑"
                    ToolTip="图片调整" Click="AdjustButton_Click" />
            <Button x:Name="SettingsButton" Style="{StaticResource IslandButton}" Width="26" Height="26" FontSize="14" Content="⚙"
                    ToolTip="后台设置" Click="SettingsButton_Click" />
            <Button x:Name="AboutButton" Style="{StaticResource IslandButton}" Width="26" Height="26" FontSize="14" Content="i"
                    ToolTip="关于" Click="AboutButton_Click" />
        </StackPanel>
    </Grid>
</Grid>
```

---

### R33 背景颜色与透明度设置

**需求原文摘要**：后台增加背景颜色、透明度等设置，保证体积最小、资源最小、图片质量最高。

**实现状态**：已实现。透明语义在用户后续反馈中已修正：透明度只作用于**面板背景**，图片与控件保持完全不透明（详见 R40）。

**实现逻辑**：颜色以 `#RRGGBB` 字符串保存；背景不透明度限制 0.2-1.0；保存后调用 `App.ApplySettings`，遍历所有面板应用。颜色解析失败时保留旧值，避免把面板弄黑/透明到不可用。

**实现方式**：`AppSettings.cs`、`SettingsWindow.xaml(.cs)`、`MainWindow.xaml.cs`。

```csharp
public string BackgroundColor { get; set; } = "#101216";
public double PanelOpacity { get; set; } = 1.0;
```

```csharp
public void ApplyVisualSettings(AppSettings settings)
{
    try
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
        var alpha = (byte)Math.Round(Math.Clamp(settings.PanelOpacity, 0, 1) * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        Root.Background = brush;
        // The window itself stays fully transparent. Only the panel background receives
        // the configured alpha, so the image and controls remain fully opaque.
        Background = System.Windows.Media.Brushes.Transparent;
    }
    catch
    {
        // keep the previous valid colour
    }

    if (App.Settings.Adjustments != null && _sourcePixels == null)
        _adjustments = App.Settings.Adjustments.Clone();
    ApplyCornerRadius(settings.CornerRadius);
}
```

```csharp
private void PickColor_Click(object sender, RoutedEventArgs e)
{
    using var dialog = new ColorDialog { FullOpen = true };
    try
    {
        var current = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
            .ConvertFromString(BackgroundColorBox.Text);
        dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
    }
    catch { }

    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        BackgroundColorBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
}
```

---

### R36 严谨开发档案

**需求原文摘要**：整理所有对话中的每一个功能、细节需求，形成严谨开发档案；每项下面附实现逻辑、实现方式、代码片段；每次项目更新都同步更新档案；任何 AI 读取后可完全重建同版本。

**实现状态**：本文件即实现。

**实现逻辑**：
1. 以对话中提取的全部真实用户需求为条目建立 `R01`-`R49`（后续新增需求继续顺延）。
2. 每条记录需求摘要、状态、实现逻辑、实现方式和当前代码片段。
3. 历史踩坑与禁止回退项在对应条目中写明。
4. 强制维护规则置于文档顶部，约束后续 AI/开发者。

**实现方式**：`DEVELOPMENT.md`。

**代码/文档片段**：本文件全文。每次修改后必须重新核对：代码片段是否仍与对应源文件一致、需求索引状态是否正确、版本变更记录是否追加。

---

### R37 按截图改造刘海：白色细线 → 平滑展开的截图式刘海

**需求原文摘要**：以截图样式构建顶部刘海菜单；平时是白色细线长条停靠顶部中间；鼠标悬停后，以截图外形向下平滑伸展。

**实现状态**：已实现。

**实现逻辑**：
1. `IslandHitArea` 是 232x46 的透明悬停热区，保证细线状态下仍能触发悬停。
2. `IslandRoot` 是 232x34 的完整岛形内容；此前 252x48 的对称药丸、232x32 的宽顶斜侧形态均已被用户指出不符合截图，现按 R45 重建。
3. 截图逐行像素分析结果：顶边约 220px 宽，高约 30px；顶边向两侧延伸，连接主体处出现内圆角肩部；下方主体向内收拢，底部以大圆角闭合。
4. `RectangleGeometry IslandClip` 初始高度 2，只露出顶部白色细线；`IslandHairline` 保证折叠状态是一条干净的白色细线。
5. 鼠标进入热区时用 `RectAnimation` 把裁剪矩形高度动画到 `IslandRoot.Height`（34），露出重建后的岛形；同时按钮组淡入并启用命中测试。
6. 鼠标离开时反向动画回高度 2，按钮淡出并禁用命中测试。
7. 动画只改 clip 与 opacity，不触发布局重排，保持低资源。

**实现方式**：`MainWindow.xaml` 结构见 R32；`MainWindow.xaml.cs` 动画逻辑如下。

```csharp
IslandHitArea.MouseEnter += (_, _) => AnimateIsland(true);
IslandHitArea.MouseLeave += (_, _) => AnimateIsland(false);
```

```csharp
/// <summary>
/// Reveals the screenshot-style island by animating a rectangular clip from the
/// collapsed hairline down to the full rounded shape. No layout properties animate,
/// so the panel stays cheap to composite.
/// </summary>
private void AnimateIsland(bool expand)
{
    var targetHeight = expand ? IslandRoot.Height : 2;
    var clipAnimation = new RectAnimation(
        new Rect(0, 0, IslandRoot.Width, targetHeight),
        TimeSpan.FromMilliseconds(expand ? 260 : 190))
    {
        EasingFunction = new QuadraticEase
        {
            EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn
        }
    };
    IslandClip.BeginAnimation(RectangleGeometry.RectProperty, clipAnimation);

    var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 180 : 110));
    IslandButtons.BeginAnimation(UIElement.OpacityProperty, fade);
    IslandButtons.IsHitTestVisible = expand;
}
```

**视觉参数**：
- 岛形宽 232，高 34。
- 路径：`M 6,0 H 226 C 228.5,0 230,1.8 230,4 C 230,5.2 226.5,6.2 222,7 V 14 C 222,17 221,19.2 219,22 C 215,29 208,34 196,34 H 36 C 24,34 17,29 13,22 C 11,19.2 10,17 10,14 V 7 C 5.5,6.2 2,5.2 2,4 C 2,1.8 3.5,0 6,0 Z`。
- 折叠裁剪高度 2。
- 展开 260ms，`QuadraticEase.EaseOut`；收起 190ms，`QuadraticEase.EaseIn`。
- 填充 `#F2353537`，描边 `#FFFFFF` 1px。
- 顶部细线 `IslandHairline` 高 2，左右边距 2，圆角半径 1。
- 五个按钮各 26x26，时钟宽 40，适配 34px 岛形高度；置顶按钮已在 R43 移出。
- 禁止回退到 252x48 对称药丸形或 232x32 宽顶斜侧版本；它们都不是用户截图中的灵动岛轮廓。

---

### R38 图片悬停保持系统指针，左键拖动使用系统抓取手型

**需求原文摘要**：鼠标平时悬停于图片上时状态不变，保持指针；点击左键按动拖动图片位置时使用系统抓取手势鼠标状态，而不是重新设计抓取形态。

**实现状态**：已实现。

**实现逻辑**：
1. `Root` 与 `ImageView` 显式 `Cursor="Arrow"`，图片区域无悬停光标覆盖。
2. 删除 `Assets/grab.cur`，从 `.csproj` 移除资源，不再加载任何自绘 `.cur`。
3. 左键平移或配置的窗口拖动手势开始时调用 `SetGrabCursor()`，把窗口光标设为 Windows 内置 `Cursors.Hand`。
4. 松开/丢失捕获后调用 `RestoreCursor()` 恢复 `Cursors.Arrow`。
5. 按钮仍保留自己的 `Cursor="Hand"`，但按钮点击在 `PreviewMouseDown` 中被 `IsFromButton` 放行，不会被当成图片平移。

**实现方式**：`MainWindow.xaml(.cs)`、`.csproj`。

```xml
<Grid x:Name="Root" Background="#00000000" ClipToBounds="True" AllowDrop="True" Cursor="Arrow">
    <Canvas x:Name="ImageViewport" Background="Transparent" ClipToBounds="True">
        <Image x:Name="ImageView"
               Stretch="Fill"
               Cursor="Arrow"
               RenderOptions.BitmapScalingMode="HighQuality" />
    </Canvas>
```

```csharp
/// <summary>
/// Use Windows' built-in hand cursor while dragging; no custom .cur resource is loaded.
/// Hovering over the image keeps the normal arrow because no Cursor override is active.
/// </summary>
private void SetGrabCursor() => Cursor = Cursors.Hand;

private void RestoreCursor() => Cursor = Cursors.Arrow;
```

```csharp
private static bool IsFromButton(RoutedEventArgs e)
{
    var element = e.OriginalSource as DependencyObject;
    while (element != null)
    {
        if (element is System.Windows.Controls.Button) return true;
        element = VisualTreeHelper.GetParent(element);
    }
    return false;
}
```

**历史变更**：之前版本使用内嵌 `Assets/grab.cur` 自绘抓取光标；最新需求明确要求改为系统抓取手势，因此该资源已删除。禁止再次引入自定义 `.cur`。

---

### R39 封装产物文件名包含版本号

**需求原文摘要**：给封装的软件命名中添加版本号。

**实现状态**：已实现。

**实现逻辑**：
1. 两个封装 bat 都从 `.csproj` 的 `<Version>` 节点读取当前版本号，避免版本号硬编码在多处。
2. 单文件运行包主产物命名为 `GuaishushuViewer-v<Version>.exe`，例如 `GuaishushuViewer-v1.2.0.exe`。
3. 同时生成固定名称 `GuaishushuViewer.exe`，供安装脚本、快捷方式和历史调用方式兼容使用。
4. ZIP 安装包命名为 `GuaishushuViewer-v<Version>-Setup.zip`，例如 `GuaishushuViewer-v1.2.0-Setup.zip`。
5. 同时保留固定名称 `dist/GuaishushuViewer-Setup.zip` 作为兼容副本。
6. 若 `<Version>` 解析失败，bat 回退到 `1.2.0`，保证脚本仍可运行。

**实现方式**：`封装单文件运行包.bat`、`封装安装包.bat`。

```bat
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION set "VERSION=1.2.0"
set "VERSIONED_EXE=%ROOT%GuaishushuViewer-v%VERSION%.exe"
```

```bat
set "FINAL=%ROOT%dist\GuaishushuViewer-v%VERSION%-Setup.zip"
set "STABLE=%ROOT%dist\GuaishushuViewer-Setup.zip"
copy /Y "%PACKAGE%" "%FINAL%" >nul
copy /Y "%PACKAGE%" "%STABLE%" >nul
```

**当前产物**：
- `GuaishushuViewer-v1.2.0.exe`
- `dist/GuaishushuViewer-v1.2.0-Setup.zip`
- 兼容副本：`GuaishushuViewer.exe`、`dist/GuaishushuViewer-Setup.zip`

---

### R40 窗体背景透明，但图片保持不透明

**需求原文摘要**：软件窗口没有真正的透明度调节；调节后整个软件界面（包括图片）都变透明。用户强调：要的是**软件面板透明、图片不透明**；窗体没有被图片覆盖的地方才按设定透明度显示。

**实现状态**：已实现。

**实现逻辑**：
1. 旧实现把 `PanelOpacity` 写到 `Window.Opacity`，导致整窗（含图片）一起透明，这是错误行为。
2. 新实现让 `Window` 本身完全透明：XAML 设置 `AllowsTransparency=True`、`Background=Transparent`。
3. `PanelOpacity` 转换为 Alpha 通道，只写入 `Root.Background` 的 `SolidColorBrush`：`Color.FromArgb(alpha, R, G, B)`。
4. `ImageView` 与所有控件不设置整体透明度，因此图片始终 100% 不透明；未被图片覆盖的面板区域按 Alpha 透视桌面。
5. `WindowChrome` 保留 8px 原生缩放边框；透明窗口仍支持四边四角缩放。
6. 设置窗口标签明确写为“面板背景不透明度（图片不受影响）”，避免歧义。

**实现方式**：`MainWindow.xaml`、`MainWindow.xaml.cs`、`SettingsWindow.xaml(.cs)`。

```xml
<Window x:Class="WBImageViewer.MainWindow"
        WindowStyle="None"
        AllowsTransparency="True"
        ResizeMode="CanResize"
        Background="Transparent">

    <shell:WindowChrome CaptionHeight="0"
                        ResizeBorderThickness="8"
                        GlassFrameThickness="0"
                        UseAeroCaptionButtons="False" />

    <Grid x:Name="Root" Background="#00000000" ClipToBounds="True" AllowDrop="True" Cursor="Arrow">
        <Canvas x:Name="ImageViewport" Background="Transparent" ClipToBounds="True">
            <Image x:Name="ImageView" Stretch="Fill" Cursor="Arrow" />
        </Canvas>
```

```csharp
var baseColor = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
var alpha = (byte)Math.Round(Math.Clamp(settings.PanelOpacity, 0, 1) * 255);
var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
Root.Background = brush;
// The window itself stays fully transparent. Only the panel background receives
// the configured alpha, so the image and controls remain fully opaque.
Background = System.Windows.Media.Brushes.Transparent;
```

```xml
<TextBlock x:Name="OpacityLabel"
           Text="面板背景不透明度 100%（图片不受影响）"
           FontWeight="SemiBold" Margin="0,0,0,6" />
```

```csharp
OpacityLabel.Text = $"面板背景不透明度（图片不受影响） {OpacitySlider.Value:P0}";
```

**禁止回退**：不要再设置 `Window.Opacity = settings.PanelOpacity`；那会让图片一起透明，直接违反本需求。

---

### R41 软件名称更正为“怪叔叔看图”

**需求原文摘要**：用户澄清“我前面写错了，我叫怪叔叔，这个是我自己的软件”，正确名称必须是“怪叔叔看图”，不是“该怪叔叔看图”。

**实现状态**：已实现。

**实现逻辑**：此前根据错误输入写入了“该怪叔叔看图”；本次把中文显示名全项目统一更正。`AppInfo.Name`、`.csproj` 的 `Product/Authors`、窗口 Title、关于页、文件关联 ProgID 显示名、安装脚本注册表值、README/LICENSE/DONATE/开发档案、两个封装 bat 全部同步。英文技术标识 `GuaishushuViewer`、`WBImageViewer`、仓库 URL 保持不变，避免破坏已有安装路径与单实例管道。

**实现方式**：全项目文本与资源。

```csharp
// Models/AppInfo.cs
public const string Name = "怪叔叔看图";
```

```xml
<!-- WBImageViewer.csproj -->
<Product>怪叔叔看图</Product>
<Authors>怪叔叔</Authors>
```

**禁止回退**：任何新文件、新文档、新提示文案都不得再写“该怪叔叔看图”；历史记录中的旧名称仅作为变更原因保留。

---

### R42 托盘右键菜单版本文本分行

**需求原文摘要**：托盘右键菜单的版本号显示文本太长，被窗体切割；要求把软件名和版本号分行。

**实现状态**：已实现。

**实现逻辑**：`ToolStripLabel` 改为两行文本，`AutoSize=false`，固定 `154x34`，居中对齐，避免单行文本被菜单宽度裁切。托盘图标悬停提示仍保留单行，不受影响。

**实现方式**：`App.xaml.cs`。

```csharp
var status = new ToolStripLabel($"{AppInfo.Name}{Environment.NewLine}v{AppInfo.Version}")
{
    AutoSize = false,
    Size = new System.Drawing.Size(154, 34),
    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
    ForeColor = System.Drawing.Color.FromArgb(0x70, 0x75, 0x80),
    Font = new System.Drawing.Font("Segoe UI", 8)
};
menu.Items.Add(status);
```

---

### R43 置顶按钮改为图钉并并入右上角关闭伸缩区

**需求原文摘要**：把置顶按钮以图钉样式构建，并移动到关闭按钮旁边，纳入关闭按钮伸缩区。

**实现状态**：已实现。

**实现逻辑**：
1. 从 `IslandButtons` 中移除原 `⌃/⌄` 文本置顶按钮。
2. 在 `CloseHost` 内新增 `CloseStrip`，图钉按钮与关闭按钮并列，`CloseTransform` 从按钮级提升到 `StackPanel` 级，两个按钮同步伸出/收回。
3. 图钉样式经 R48 最终确定为系统 📌 Emoji（`TextBlock Text="📌" FontFamily="Segoe UI Emoji, Segoe UI Symbol"`），不再使用自绘矢量 `Path`；未置顶时通过透明度变暗表示状态。
4. 全局快捷键 `ToggleTopMost` 不再改按钮文字，只切换 `Topmost` 并调用 `UpdatePinState()`。

**实现方式**：`MainWindow.xaml(.cs)`。按钮 XAML 见 R25；状态代码如下。

```csharp
/// <summary>The pushpin dims when topmost is off; the glyph itself stays a pushpin.</summary>
private void UpdatePinState()
{
    PinButton.Opacity = Topmost ? 1 : 0.5;
    PinButton.ToolTip = Topmost
        ? "取消置顶 (Shift+Ctrl+Alt+T)"
        : "置顶窗口 (Shift+Ctrl+Alt+T)";
}

case HotkeyAction.ToggleTopMost:
    Topmost = !Topmost;
    UpdatePinState();
    break;
```

---

### R44 图片画布始终以窗口当前大小为视线范围

**需求原文摘要**：缩放图片时窗口错乱，被无形窗口切割再盖图片；要求图片画布界面始终以窗口当前大小为视线范围。

**实现状态**：已实现。

**实现逻辑**：
1. 旧实现把大尺寸 `Image` 直接放在 `Root` 中，再用 `TranslateTransform` 平移；过大图片会参与布局/命中测试，与 `WindowChrome` 的非客户区叠加，出现“无形窗口切割并盖住图片”的错乱。
2. 新增 `ImageViewport` Canvas：`ClipToBounds=True`，尺寸等于窗口客户区，图片作为 Canvas 子元素定位，不再影响窗口布局。
3. 几何状态改为 `_zoom + _offsetX + _offsetY`；`ApplyImagePosition` 用当前视口尺寸重新居中并叠加偏移。
4. `ImageViewport.SizeChanged` 触发 `ApplyImagePosition`，窗口尺寸变化时画布立刻按新客户区重算。
5. 滚轮缩放、左键平移都以 `ImageViewport` 坐标计算，锚点算法保持不漂移（见 R04/R15）。

**实现方式**：`MainWindow.xaml(.cs)`。

```xml
<Canvas x:Name="ImageViewport"
        Background="Transparent"
        ClipToBounds="True"
        SnapsToDevicePixels="False">
    <Image x:Name="ImageView" Stretch="Fill" Cursor="Arrow" />
</Canvas>
```

```csharp
ImageViewport.SizeChanged += (_, _) => ApplyImagePosition();

private void ApplyImagePosition()
{
    if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
    var centerX = (ImageViewport.ActualWidth - ImageView.Width) / 2.0;
    var centerY = (ImageViewport.ActualHeight - ImageView.Height) / 2.0;
    Canvas.SetLeft(ImageView, centerX + _offsetX);
    Canvas.SetTop(ImageView, centerY + _offsetY);
}
```

**禁止回退**：不要恢复 `ImageTranslate`/`TranslateTransform` 作为唯一平移手段；不要让图片脱离 `ImageViewport` 直接成为 `Root` 子元素。

---

### R45 按截图重建灵动岛轮廓

**需求原文摘要**：灵动岛外形仍不对；要求仔细分析截图，按“底部呈现收拢并圆角，上部向两边延伸并有内圆角”的线条重新构建。

**实现状态**：已废弃，由 R46 替代（用户随后提供了 ASCII 外形模型，本条路径已不再使用）。

**历史实现要点**：曾按截图分析出“顶部横向外扩 + 肩部内圆角 + 下部收拢 + 底部大圆角”，用 `M 6,0 H 226 C ...` 单条 Path 实现，岛高 34、热区 232x46。该形态的翼展与内圆角幅度不够明显，被 R46 的 240x40 双翼路径取代。禁止再把此路径当作当前实现。

---

## 6. 图片增强与调整

### R34 去噪与锐化增强

**需求原文摘要**：采取优秀算法进一步优化显示效果，增强去噪波、杂点能力，提升锐化和清晰度。

**实现状态**：已实现。

**实现逻辑**：
- 去噪：边缘保护 3x3 均值滤波。只混合与中心像素色差小于阈值的邻域，避免糊掉真实边缘。
- 锐化：3x3 高斯模糊生成反锐化掩模，再按强度把高频差加回原图。
- 全部使用 `int[]` BGRA 像素，无第三方依赖。
- 透明像素跳过。

**实现方式**：`Services/ImageEnhanceService.cs`。

```csharp
var denoise = adjustments.Denoise;
if (denoise > 0)
    EdgeAwareDenoise(destination, width, height, denoise);
```

```csharp
/// <summary>Edge-aware 3x3 smoothing: noise is reduced without flattening real edges.</summary>
private static void EdgeAwareDenoise(int[] pixels, int width, int height, int amount)
{
    var blend = amount / 100.0 * 0.62;
    var threshold = 6 + amount / 8;
    var copy = (int[])pixels.Clone();

    for (var y = 0; y < height; y++)
    {
        var row = y * width;
        for (var x = 0; x < width; x++)
        {
            var index = row + x;
            var center = copy[index];
            var cr = center >> 16 & 0xFF;
            var cg = center >> 8 & 0xFF;
            var cb = center & 0xFF;
            var sumR = 0;
            var sumG = 0;
            var sumB = 0;
            var count = 0;

            for (var dy = -1; dy <= 1; dy++)
            {
                var ny = y + dy;
                if (ny < 0 || ny >= height) continue;
                var nrow = ny * width;
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    if (nx < 0 || nx >= width) continue;
                    var n = copy[nrow + nx];
                    var nr = n >> 16 & 0xFF;
                    var ng = n >> 8 & 0xFF;
                    var nb = n & 0xFF;
                    var distance = Math.Abs(nr - cr) + Math.Abs(ng - cg) + Math.Abs(nb - cb);
                    if (distance > threshold) continue;
                    sumR += nr;
                    sumG += ng;
                    sumB += nb;
                    count++;
                }
            }

            if (count < 2) continue;
            var inv = 1.0 / count;
            var ar = sumR * inv;
            var ag = sumG * inv;
            var ab = sumB * inv;
            var alpha = (int)((uint)center >> 24);
            pixels[index] = (alpha << 24) |
                (Clamp((int)(cr + (ar - cr) * blend)) << 16) |
                (Clamp((int)(cg + (ag - cg) * blend)) << 8) |
                Clamp((int)(cb + (ab - cb) * blend));
        }
    }
}
```

```csharp
var sharpen = adjustments.Sharpen;
if (sharpen <= 0) return;

var blur = new int[destination.Length];
GaussianBlur(destination, blur, width, height);
var strength = sharpen / 100.0 * 0.85;

for (var i = 0; i < destination.Length; i++)
{
    var pixel = destination[i];
    if ((pixel & unchecked((int)0xFF000000)) == 0) continue;
    var blurred = blur[i];
    var a = (int)((uint)pixel >> 24);
    var r = Clamp((int)((pixel >> 16 & 0xFF) + ((pixel >> 16 & 0xFF) - (blurred >> 16 & 0xFF)) * strength));
    var g = Clamp((int)((pixel >> 8 & 0xFF) + ((pixel >> 8 & 0xFF) - (blurred >> 8 & 0xFF)) * strength));
    var b = Clamp((int)((pixel & 0xFF) + ((pixel & 0xFF) - (blurred & 0xFF)) * strength));
    destination[i] = (a << 24) | (r << 16) | (g << 8) | b;
}
```

---

### R35 PS 式调整、智能优化与一键还原

**需求原文摘要**：借鉴 PS 提供亮度、对比度、色差等调整；去噪/锐化也放进同一面板；提供合适预置值；一键还原初始效果；打开图片时智能优化；手动归零恢复原始。

**实现状态**：已实现。

**实现逻辑**：
1. `AdjustPanel` 提供五个滑块：亮度、对比度、饱和度（用户称色差）、去噪、锐化。
2. `Adjustment_Changed` 更新 `_adjustments` 与 `App.Settings.Adjustments`，并启动 160ms 防抖计时器。
3. 打开图片时：若 `AutoOptimize=true`，使用 `ImageAdjustments.SmartPreset()`；否则用上次保存参数。
4. “智能优化”按钮应用 `SmartPreset`。
5. “还原原始”按钮把五项全部归零；`ImageEnhanceService.Apply` 在参数全零时只复制源像素，因此恢复到原始像素。

**实现方式**：`MainWindow.xaml` 调整抽屉、`MainWindow.xaml.cs`、`Models/AppInfo.cs`、`Services/ImageEnhanceService.cs`。

```xml
<Border x:Name="AdjustPanel"
        Visibility="Collapsed"
        Width="232"
        HorizontalAlignment="Right"
        VerticalAlignment="Top"
        Margin="0,48,8,0"
        Padding="12,10,12,12"
        CornerRadius="12"
        Background="#E6161A20"
        BorderBrush="#33FFFFFF"
        BorderThickness="1">
    <StackPanel>
        <DockPanel>
            <TextBlock Text="图片调整" FontSize="12" FontWeight="SemiBold" DockPanel.Dock="Left" />
            <Button Content="×" Width="22" Height="22" Padding="0" HorizontalAlignment="Right"
                    Style="{StaticResource IslandButton}" Click="CloseAdjust_Click" />
        </DockPanel>
        <TextBlock Style="{StaticResource SliderLabel}" Text="亮度 0" x:Name="BrightnessLabel" />
        <Slider x:Name="BrightnessSlider" Minimum="-100" Maximum="100" ValueChanged="Adjustment_Changed" />
        <TextBlock Style="{StaticResource SliderLabel}" Text="对比度 0" x:Name="ContrastLabel" />
        <Slider x:Name="ContrastSlider" Minimum="-100" Maximum="100" ValueChanged="Adjustment_Changed" />
        <TextBlock Style="{StaticResource SliderLabel}" Text="饱和度 0" x:Name="SaturationLabel" />
        <Slider x:Name="SaturationSlider" Minimum="-100" Maximum="100" ValueChanged="Adjustment_Changed" />
        <TextBlock Style="{StaticResource SliderLabel}" Text="去噪 0" x:Name="DenoiseLabel" />
        <Slider x:Name="DenoiseSlider" Minimum="0" Maximum="100" ValueChanged="Adjustment_Changed" />
        <TextBlock Style="{StaticResource SliderLabel}" Text="锐化 0" x:Name="SharpenLabel" />
        <Slider x:Name="SharpenSlider" Minimum="0" Maximum="100" ValueChanged="Adjustment_Changed" />
        <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
            <Button Content="智能优化" Style="{StaticResource SmallButton}" Width="92" Margin="0"
                    Click="SmartPreset_Click" />
            <Button Content="还原原始" Style="{StaticResource SmallButton}" Width="92" Click="ResetAdjust_Click" />
        </StackPanel>
    </StackPanel>
</Border>
```

```csharp
_adjustTimer = new DispatcherTimer(DispatcherPriority.Render)
{
    Interval = TimeSpan.FromMilliseconds(160)
};
_adjustTimer.Tick += (_, _) =>
{
    _adjustTimer.Stop();
    ApplyAdjustments();
};
```

```csharp
_adjustments = App.Settings.AutoOptimize
    ? ImageAdjustments.SmartPreset()
    : (App.Settings.Adjustments ?? new ImageAdjustments()).Clone();
```

```csharp
private void Adjustment_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (_updatingControls) return;
    _adjustments = new ImageAdjustments
    {
        Brightness = (int)Math.Round(BrightnessSlider.Value),
        Contrast = (int)Math.Round(ContrastSlider.Value),
        Saturation = (int)Math.Round(SaturationSlider.Value),
        Denoise = (int)Math.Round(DenoiseSlider.Value),
        Sharpen = (int)Math.Round(SharpenSlider.Value)
    };
    App.Settings.Adjustments = _adjustments.Clone();
    UpdateAdjustmentLabels();
    _adjustTimer.Stop();
    _adjustTimer.Start();
}

private void SmartPreset_Click(object sender, RoutedEventArgs e)
{
    _adjustments = ImageAdjustments.SmartPreset();
    App.Settings.Adjustments = _adjustments.Clone();
    UpdateAdjustmentControls();
    ApplyAdjustments();
}

private void ResetAdjust_Click(object sender, RoutedEventArgs e)
{
    _adjustments = new ImageAdjustments();
    App.Settings.Adjustments = _adjustments.Clone();
    UpdateAdjustmentControls();
    ApplyAdjustments();
}
```

```csharp
// ImageEnhanceService.cs
Array.Copy(source, destination, source.Length);
if (width <= 0 || height <= 0) return;

var brightness = adjustments.Brightness;
var contrast = adjustments.Contrast;
var saturation = adjustments.Saturation;
var contrastFactor = (100 + contrast) / 100.0;
var saturationFactor = (100 + saturation) / 100.0;
var brightnessDelta = brightness * 2;

r = Clamp((int)((r + brightnessDelta - 128) * contrastFactor + 128));
g = Clamp((int)((g + brightnessDelta - 128) * contrastFactor + 128));
b = Clamp((int)((b + brightnessDelta - 128) * contrastFactor + 128));

if (saturation != 0)
{
    var gray = (r * 77 + g * 150 + b * 29) >> 8;
    r = Clamp((int)(gray + (r - gray) * saturationFactor));
    g = Clamp((int)(gray + (g - gray) * saturationFactor));
    b = Clamp((int)(gray + (b - gray) * saturationFactor));
}
```

---

## 7. 后台设置与设置校验

### 设置窗口字段

`SettingsWindow.xaml` 提供：
- 单窗口模式
- 移动窗口方式
- 三个全局快捷键录制框
- 滚轮同步缩放修饰键
- 缩放步长
- 圆角半径
- 背景颜色
- 透明度
- 自动智能优化
- 默认关联注册
- 打开 Windows 默认程序选择对话框

### 快捷键录制

```csharp
private static void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
{
    e.Handled = true;
    var box = (TextBox)sender;

    if (e.Key == Key.Escape)
    {
        box.Text = string.Empty;
        return;
    }

    if (e.Key is Key.LeftShift or Key.RightShift or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin)
        return;

    var parts = new List<string>();
    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
    parts.Add(e.Key.ToString());
    box.Text = string.Join("+", parts);
}
```

### 保存校验

```csharp
if (!HotkeySpec.TryParse(topMost, out _, out _) ||
    !HotkeySpec.TryParse(fitWindow, out _, out _) ||
    !HotkeySpec.TryParse(fitImage, out _, out _))
{
    MessageBox.Show("快捷键必须包含 Shift / Ctrl / Alt 中至少一个修饰键，以及一个普通按键。",
        "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
}

var hotkeys = new[] { topMost, fitWindow, fitImage };
if (hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
{
    MessageBox.Show("三个全局快捷键不能重复。", "设置无效",
        MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
}
```

---

## 8. Win32 互操作清单

`src/WBImageViewer/Native/Win32.cs` 当前常量与函数：

```csharp
public const int WM_HOTKEY = 0x0312;
public const int WM_NCHITTEST = 0x0084;
public const int WM_GETMINMAXINFO = 0x0024;

public const uint MOD_ALT = 0x0001;
public const uint MOD_CONTROL = 0x0002;
public const uint MOD_SHIFT = 0x0004;

public const uint SWP_NOSIZE = 0x0001;
public const uint SWP_NOZORDER = 0x0004;
public const uint SWP_NOACTIVATE = 0x0010;

public const int VK_SHIFT = 0x10;
public const int VK_CONTROL = 0x11;
public const int VK_MENU = 0x12;
public const int VK_SPACE = 0x20;

[DllImport("user32.dll", SetLastError = true)]
public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll", SetLastError = true)]
public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

[DllImport("user32.dll")]
public static extern short GetKeyState(int nVirtKey);

[DllImport("user32.dll")]
public static extern bool GetCursorPos(out POINT lpPoint);

[DllImport("user32.dll")]
public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

[DllImport("user32.dll")]
public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

[DllImport("user32.dll")]
public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

[DllImport("gdi32.dll")]
public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

[DllImport("dwmapi.dll")]
public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

public static bool IsKeyDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;
```

---

## 9. 历史问题与处置档案

| 问题 | 根因 | 处置 | 当前约束 |
| --- | --- | --- | --- |
| 双击图片未用本软件打开 | Win11 `UserChoice` 受保护，静默写入会回滚 | 注册 ProgID/OpenWith + 系统“打开方式”确认 | 禁止伪造 Hash；不要静默删除用户默认选择 |
| 拖图失效 | 未设置 `AllowDrop` | Window/Root 均允许拖放并补齐事件 | 新增容器时必须保留 `AllowDrop` |
| 顶部白边 | 自定义非客户区绘制与 `WindowChrome` 冲突 | 移除自定义钩子，只保留 `WindowChrome` | 禁止恢复旧 `WM_NCHITTEST` 方案 |
| 圆角过大/锯齿 | 默认系统圆角大；GDI region 锯齿 | DWM 圆角偏好 + 后台 0-24 设置 | 不创建新 GDI region |
| 图标丑/托盘无 logo | 资源缺失/回退系统图标 | 多尺寸 ICO 内嵌，托盘资源优先 | 保留资源优先、exe 回退 |
| 图片适配异常 | `TransformGroup` 缩放中心不可控；大图参与 Root 布局 | 改显式 `Width/Height` + `_zoom` + `ImageViewport` 画布裁切 | 禁止再引入中心缩放变换或让大图脱离视口 |
| 滚轮锚点漂移 | 以图片中心为隐含原点 | 鼠标相对坐标反算偏移 | 保持 `ZoomAt` 算法 |
| 按钮点击被平移拦截 | `PreviewMouseDown` 未识别按钮 | `IsFromButton` 放行 | 新按钮需位于按钮视觉树内 |
| IExpress 不稳 | 中文路径/环境兼容问题 | ZIP + `install.cmd` | 不改回 IExpress |
| bat 中文乱码/暂停 | cmd 编码与交互暂停 | 文件转 GBK；`--ci` 跳过暂停 | 修改 bat 后保持 GBK |
| Git push 失败 | TLS/低速限制 | GitHub API 上传 blob/tree/commit/ref 与 Release | 如再失败先检查网络，再走 API |
| 自绘抓取光标不符合要求 | 用户要求使用系统抓取手势 | 删除 `grab.cur`，使用 `Cursors.Hand` | 禁止重新引入自定义 `.cur` |

---

## 10. 可复现构建与发布流程

### 10.1 本地构建

```bash
dotnet build "H:/无窗口图片浏览软件（WB）/src/WBImageViewer/WBImageViewer.csproj" -c Release --nologo
```

### 10.2 单文件运行包

```bat
封装单文件运行包.bat --ci
```

输出：
- `GuaishushuViewer-v1.2.0.exe`（带版本号）
- `GuaishushuViewer.exe`（兼容副本）
- `dist/single/GuaishushuViewer.exe`

### 10.3 安装包

```bat
封装安装包.bat --ci
```

输出：
- `dist/GuaishushuViewer-v1.2.0-Setup.zip`（带版本号）
- `dist/GuaishushuViewer-Setup.zip`（兼容副本）

### 10.4 发布前检查清单

1. `AppInfo.Version`、`.csproj Version/AssemblyVersion/FileVersion`、`AboutWindow`、`README.md`、托盘状态一致。
2. `DEVELOPMENT.md` 中所有代码片段与源码一致。
3. `dotnet build` 0 错误；最好 0 警告。
4. 单文件与 ZIP 安装包本机实测：
   - 双击图片能打开；
   - 拖拽能打开；
   - 三个快捷键生效；
   - 刘海细线/展开动画正常；
   - 右上关闭按钮悬停正常；
   - 左键拖动图片时是系统手型，松手恢复箭头；
   - 图片调整、智能优化、还原正常。
5. GitHub Release 上传新资产，并在本档案中记录资产名与链接。

---

### R46 按 ASCII 外形模型重构灵动岛

**需求原文摘要**：用户提供模型 `）              (` / `|                 |` / `( ___________)`，要求按此重构窗口顶部灵动岛外形。

**实现状态**：已废弃，由 R49 替代。翼状外伸形态被用户指出有“向外扩展的尾部”“像窗口的另一个部分”，当前生效实现见 R49。下方片段仅作历史记录，禁止直接采用。

**实现逻辑**：
1. 模型解读：顶边向左右两侧伸出“翅膀”（`）/(` 两端），翅膀下沿通过**内凹圆角**过渡到下方竖直侧边；两侧边竖直（`| |`）；底部向内收拢并以凸圆角收口（`( ___ )`）。
2. 用单条 WPF `Path` 描述：整体 240x40，热区 240x52；顶边 `M 8,0 H 232`，两翼下沿到 `y=6.8`，肩部用 `Q 212,6.8 212,12.5`（右）与 `Q 28,6.8 22,6.8`（左）形成内圆角；侧边 `V 24`；底部用 `C 212,33 203.5,40 189,40` 与镜像曲线收拢成圆底。
3. 保留原有机制：`RectangleGeometry IslandClip` 初始高 2 显示白色细线，悬停用 `RectAnimation` 展开到 `IslandRoot.Height`(40)，按钮组淡入。
4. R37/R45 中的 232x46/232x34 热区与岛形已废弃，禁止回退。

**实现方式**：`MainWindow.xaml`。

```xml
<Grid x:Name="IslandHitArea"
      Width="240" Height="52"
      HorizontalAlignment="Center" VerticalAlignment="Top"
      Background="Transparent" SnapsToDevicePixels="False">
    <Grid x:Name="IslandRoot"
          Width="240" Height="40"
          HorizontalAlignment="Center" VerticalAlignment="Top">
        <Grid.Clip>
            <RectangleGeometry x:Name="IslandClip" Rect="0,0,240,2" />
        </Grid.Clip>
        <Path x:Name="IslandShape" Stretch="Fill"
              Data="M 8,0 H 232 C 233.5,0 234,1 234,2.5 V 5.5 C 234,6.3 233.3,6.8 232.5,6.8 H 218 Q 212,6.8 212,12.5 V 24 C 212,33 203.5,40 189,40 H 51 C 36.5,40 28,33 28,24 V 12.5 Q 28,6.8 22,6.8 H 7.5 C 6.7,6.8 6,6.3 6,5.5 V 2.5 C 6,1 6.5,0 8,0 Z"
              Fill="#F2353537" Stroke="#FFFFFF" StrokeThickness="1">
```
**验证**：悬停截图确认顶部双翼展开、肩部内圆角、直边、底部收拢圆角，形态与模型一致。

---

### R47 窗口适配图片后画布无留白

**需求原文摘要**：执行“按图片当前大小调整窗口”后画布留白，需窗口四边快速调整到与图片当前大小重合。

**实现状态**：已实现。

**实现逻辑**：
1. 根因：`WindowChrome` 的不可见缩放边框使窗口外框大于客户区；旧实现把图片显示尺寸直接当作外框尺寸，导致客户区与图片尺寸不一致而留白/裁切。
2. 先用 `GetWindowRect` 外框尺寸减去 `ImageViewport.ActualWidth * DpiScale` 得到四周边框差值 `borderX/borderY`。
3. 目标外框 = 图片当前显示尺寸（`_pixelWidth*_zoom*DpiScale`，`_pixelHeight*_zoom*DpiScale`）+ 边框差值；超过工作区时等比缩小并按缩小后客户区重算 `_zoom`。
4. 保留当前位置，`SetWindowPos` 调整尺寸；`_fittingWindow` 标志抑制布局过程中的重复定位。
5. 布局是异步的：`Dispatcher.BeginInvoke(..., DispatcherPriority.Render)` 中按最终 `ImageViewport.ActualWidth/Height` 再次锁定 `_zoom` 并清零 `_offsetX/_offsetY`，保证四边完全重合。

**实现方式**：`MainWindow.xaml.cs`。

```csharp
private bool _fittingWindow;
ImageViewport.SizeChanged += (_, _) => { if (!_fittingWindow) ApplyImagePosition(); };

private void FitWindowToImage()
{
    if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return;
    var dpi = VisualTreeHelper.GetDpi(this);
    var area = WorkAreaPhysical();

    var borderX = rect.Width - (int)Math.Round(ImageViewport.ActualWidth * dpi.DpiScaleX);
    var borderY = rect.Height - (int)Math.Round(ImageViewport.ActualHeight * dpi.DpiScaleY);
    if (borderX < 0) borderX = 0;
    if (borderY < 0) borderY = 0;

    var clientW = Math.Max(1, (int)Math.Ceiling(_pixelWidth * _zoom * dpi.DpiScaleX));
    var clientH = Math.Max(1, (int)Math.Ceiling(_pixelHeight * _zoom * dpi.DpiScaleY));
    var width = clientW + borderX;
    var height = clientH + borderY;

    var maxW = Math.Max(140, area.Width);
    var maxH = Math.Max(100, area.Height);
    var scale = Math.Min(1.0, Math.Min(maxW / (double)width, maxH / (double)height));
    if (scale < 1)
    {
        width = Math.Max(140, (int)Math.Floor(width * scale));
        height = Math.Max(100, (int)Math.Floor(height * scale));
        clientW = width - borderX;
        clientH = height - borderY;
        _zoom = Math.Min(clientW / (_pixelWidth * dpi.DpiScaleX),
                         clientH / (_pixelHeight * dpi.DpiScaleY));
        ApplyImageSize();
    }

    var left = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - width));
    var top = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
    _fittingWindow = true;
    _offsetX = _offsetY = 0;
    Win32.SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height,
        Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

    Dispatcher.BeginInvoke(new Action(() =>
    {
        var vw = ImageViewport.ActualWidth;
        var vh = ImageViewport.ActualHeight;
        if (vw > 8 && vh > 8)
        {
            _zoom = Math.Min(vw / _pixelWidth, vh / _pixelHeight);
            _offsetX = _offsetY = 0;
            ApplyImageSize();
        }
        _fittingWindow = false;
    }), DispatcherPriority.Render);
}
```
**验证**：实测外框从 760x560 收缩到 760x475，截图确认图片四边与画布边缘完全重合、无留白。

---

### R48 置顶图钉改为 📌 Emoji 样式

**需求原文摘要**：当前置顶按钮图钉样式太丑，请按“📌”这个样式重构。

**实现状态**：已实现。

**实现逻辑**：移除 R43 的自绘矢量 `Path` 图钉，改为 `TextBlock Text="📌"` 并指定 `FontFamily="Segoe UI Emoji, Segoe UI Symbol"`、`FontSize=16`，直接渲染系统 emoji 图钉；按钮容器、悬停背景、点击行为与置顶状态透明度逻辑不变。

**实现方式**：`MainWindow.xaml`。

```xml
<Border x:Name="Bd" Background="#CC242830" CornerRadius="6,0,0,6">
    <TextBlock Text="📌" FontFamily="Segoe UI Emoji, Segoe UI Symbol"
               FontSize="16" HorizontalAlignment="Center"
               VerticalAlignment="Center" />
</Border>
```
**验证**：悬停截图确认右上角伸缩区显示红色 📌 图钉 + 红色关闭 X，形态与用户给出的 emoji 一致。

---

### R49 灵动岛顶边与窗口顶边相切、无外伸尾部、顶角与底角圆角等大

**需求原文摘要**：修正灵动岛外形；把顶部两个内圆圆角角度调整到下面两个外圆角的大小一致；顶部不要留向外扩展的尾部，圆窗口顶部刚好相切即可；调整后应有“刚好从窗口边缘长出来”的感觉，而不是窗口的另一个部分。

**实现状态**：已实现，替代 R46 的双翼外伸形态。

**实现逻辑**：
1. 删除 R46 路径中 `y=0` 顶边两端向外伸出的“翅膀 / 尾部”（即 `M 8,0 H 232` 两侧超出的细线，以及 `V 5.5`、`V 2.5` 形成的细长外沿）。
2. 新路径顶边为 `M 12,0 H 228`，`y=0` 直接位于 `IslandRoot` 顶部，与客户区/窗口顶边齐平相切，不再有任何超出主体宽度的细线。
3. 顶部两角用三次贝塞尔 `C 228,8.8 220.8,16 212,16`（右）与 `C 28,8.8 20.8,0 12,0`（左）收出圆角，水平与垂直跨度均为 16。
4. 底部两角 `C 212,32.8 204.8,40 196,40` 与 `C 35.2,40 28,32.8 28,24` 同样为 16 半径，保证顶角与底角圆角大小一致。
5. 折叠白色细线 `IslandHairline` 的 `Margin` 由 `2,0,2,0` 改为 `12,0,12,0`，宽度与岛顶边（12..228）对齐，避免收起状态出现外伸尾部。
6. 展开/收起仍由 `IslandClip` 高度 2↔40 的 `RectAnimation` 驱动，只改裁剪与透明度，不触发布局重排。

**实现方式**：`MainWindow.xaml`。

```xml
<Path x:Name="IslandShape" Stretch="Fill"
      Data="M 12,0 H 228 C 228,8.8 220.8,16 212,16 V 24 C 212,32.8 204.8,40 196,40 H 44 C 35.2,40 28,32.8 28,24 V 16 C 28,8.8 20.8,0 12,0 Z"
      Fill="#F2353537" Stroke="#FFFFFF" StrokeThickness="1">
    <Path.Effect>
        <DropShadowEffect Color="#000000" Opacity="0.38" BlurRadius="8" ShadowDepth="2" />
    </Path.Effect>
</Path>

<Rectangle x:Name="IslandHairline" Height="2" Margin="12,0,12,0"
           VerticalAlignment="Top" RadiusX="1" RadiusY="1" Fill="#FFFFFF" />
```

**验证**：悬停截图确认顶部无向外伸出的细线尾部，顶边与窗口顶边齐平，顶部两角与底部两角圆角大小一致，整体呈“从窗口边缘长出”的观感。

**禁止回退**：不要再使用 R46 的 `M 8,0 H 232 ... V 5.5 ... V 2.5` 双翼外伸路径，也不要恢复 `IslandHairline` 的 `Margin="2,0,2,0"`；那会重新出现用户明确否定的“向外扩展的尾部 / 窗口的另一个部分”。

---

---

### R50 清除生成产物，仓库回到纯源码状态

**需求原文摘要**：检查整个项目，只保留项目源码，清除所有项目源码以外的生成产物，保存项目干净。

**实现状态**：已实现。

**实现逻辑**：
1. 删除根目录的 `GuaishushuViewer.exe`、`GuaishushuViewer-v1.2.0.exe`，删除 `dist/` 整个目录。
2. 删除 `src/WBImageViewer/bin/`、`src/WBImageViewer/obj/` 等编译中间产物。
3. 保留源码 `src/`、安装源脚本 `packaging/install.cmd`、根目录两个固定封装 bat、README/DEVELOPMENT/LICENSE/DONATE、`.github/`、`.git/`、`.workbuddy/`。
4. `.gitignore` 改为忽略 `bin/`、`obj/`、`artifacts/`、`*.exe`、`.workbuddy/`、`tools/` 与截图调试产物，防止再次把生成物纳入仓库。

**实现方式**：文件清理 + `.gitignore`。

```gitignore
# Build / packaging output. Source lives in src/, packaging/ and the root scripts.
bin/
obj/
artifacts/
*.user
.vs/
.workbuddy/
tools/
*.exe
```

**验证**：清理后根目录只剩源码、文档与脚本；`dotnet build -c Release` 可重新生成，生成物仅出现在被忽略的 `bin/`、`obj/`、`artifacts/`。

---

### R51 封装专用目录与版本号单一来源

**需求原文摘要**：调整封装安装包和独立运行程序的两个批处理文件，按软件工程目录要求建立封装专用目录，产物放进去；每次调整项目都更新版本号，版本号写在源码而不是封装批处理，批处理为固定代码，运行时自动读取。

**实现状态**：已实现。

**实现逻辑**：
1. 新建统一输出根 `artifacts/`：`artifacts/single/` 放单文件 exe，`artifacts/setup/` 放 ZIP 安装包，`artifacts/setup-stage/` 为打包暂存目录。根目录不再写任何产物。
2. 两个 bat 顶部固定不变，仅用 `findstr` + `for /f tokens=3 delims=<>` 从 `src/WBImageViewer/WBImageViewer.csproj` 的 `<Version>` 读取版本号；读不到直接报错退出，不再有硬编码回退值。
3. `<Version>` 是产品版本号唯一来源：删除 `.csproj` 中重复的 `<AssemblyVersion>`/`<FileVersion>`；`AppInfo.Version` 改为从 `AssemblyInformationalVersionAttribute` 派生并按 `+` 截断；`AboutWindow.xaml` 不再硬编码版本文本。
4. 设置 `<IncludeSourceRevisionInInformationalVersion>false>`，避免 SDK 把 Git 提交哈希拼进产品版本。
5. `packaging/install.cmd` 同样保持固定代码：复制 exe 后读取该 exe 的 `VersionInfo.ProductVersion`，按 `+` 截断后写入安装提示与注册表 `DisplayVersion`，脚本本身无版本号。
6. README 的当前版本、下载文件名和输出目录说明同步到新规则。

**实现方式**：`WBImageViewer.csproj`、`Models/AppInfo.cs`、`AboutWindow.xaml`、`封装单文件运行包.bat`、`封装安装包.bat`、`packaging/install.cmd`、`README.md`、`.gitignore`。

```xml
<!-- WBImageViewer.csproj: single source of truth -->
<Version>1.3.0</Version>
<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
```

```csharp
// AppInfo.cs
public static string Version { get; } =
    typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0] ?? "1.0.0";
```

```bat
rem Fixed batch code: version comes from source on every run.
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION (
  echo [错误] 无法从 WBImageViewer.csproj 读取版本号。
  goto end
)
```

```bat
rem packaging/install.cmd: no version literal in the script.
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%DEST%\%APP%').VersionInfo.ProductVersion"`) do set "RAW_VERSION=%%V"
for /f "tokens=1 delims=+" %%V in ("%RAW_VERSION%") do set "VERSION=%%V"
```

**验证**：两个 bat 均以 `--ci` 实跑通过，产物为 `artifacts/single/GuaishushuViewer-v1.3.0.exe`、`artifacts/setup/GuaishushuViewer-v1.3.0-Setup.zip`；生成 exe 的 `ProductVersion` 为干净的 `1.3.0`。

---

### R52 置顶图钉的状态反馈

**需求原文摘要**：调整置顶，给按钮添加动态功能；激活置顶时图钉按钮变亮、为钉入状态，取消置顶时变暗、为拔起状态。

**实现状态**：已实现。

**实现逻辑**：
1. 用按钮 `Tag` 承载逻辑状态：`Pinned` / `Unpinned`，由 `UpdatePinState()` 根据 `Topmost` 写入；快捷键、按钮点击、托盘菜单都走 `ExecuteHotkey(ToggleTopMost)`，状态源唯一。
2. 控制模板内用两个 `DataTrigger` 绑定 `Tag`（`RelativeSource TemplatedParent`）：`Pinned` 为琥珀亮底 + 图钉不透明 + 0° 直立；`Unpinned` 为暗灰底 + 图钉 42% 透明度 + 35° 倾斜，形成“拔起”观感。
3. `IsMouseOver` / `IsPressed` 触发器写在状态触发器之后，保留原有悬停/按压反馈；旋转写在 `TextBlock.RenderTransform` 上而不是单独命名 `RotateTransform`，避免 WPF 模板触发器无法定位变换对象的 MC4111 编译错误。
4. ToolTip 同时随状态切换“取消置顶/置顶窗口”。

**实现方式**：`MainWindow.xaml`、`MainWindow.xaml.cs`。

```xml
<DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Value="Pinned">
    <Setter TargetName="PinBorder" Property="Background" Value="#FFD99A2B" />
    <Setter TargetName="PinGlyph" Property="Opacity" Value="1" />
    <Setter TargetName="PinGlyph" Property="RenderTransform">
        <Setter.Value><RotateTransform Angle="0" /></Setter.Value>
    </Setter>
</DataTrigger>
<DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Value="Unpinned">
    <Setter TargetName="PinBorder" Property="Background" Value="#CC242830" />
    <Setter TargetName="PinGlyph" Property="Opacity" Value="0.42" />
    <Setter TargetName="PinGlyph" Property="RenderTransform">
        <Setter.Value><RotateTransform Angle="35" /></Setter.Value>
    </Setter>
</DataTrigger>
```

```csharp
private void UpdatePinState()
{
    PinButton.Tag = Topmost ? "Pinned" : "Unpinned";
    PinButton.ToolTip = Topmost
        ? "取消置顶 (Shift+Ctrl+Alt+T)"
        : "置顶窗口 (Shift+Ctrl+Alt+T)";
}
```

**验证**：`dotnet build -c Release` 零错误零警告；切换快捷键/按钮时图钉在亮底直立与暗底倾斜间即时切换。

---

### R53 灵动岛右顶角镜像左顶角并缩小尺寸

**需求原文摘要**：顶部灵动岛右边顶部圆角方向错误，应按左边样式镜像过去，呈“上山凹进去”的感觉；现在向下凸起是反的；左边样式才是想要的；另外缩小灵动岛大小，让它更精致。

**实现状态**：已实现，替代 R49 的 240x40 外形。

**实现逻辑**：
1. R49 左肩从 `(12,0)` 到 `(28,16)` 使用“先水平后竖直”的切线（控制点 `20.8,0` 与 `28,8.8`），右肩却用“先竖直后水平”（`228,8.8` 与 `220.8,16`），所以右侧向下凸起、与左侧不对称。
2. 新尺寸 200x34：顶边 `M 10,0 H 190`，主体窄边 `x=24..176`，肩部落差 12，底部圆角半径 12。
3. 右肩改为左侧曲线的严格镜像：从 `(190,0)` 到 `(176,12)`，控制点 `(182.2,0)`、`(176,5.4)`；左肩保留为 `(24,5.4)`、`(17.8,0)`，两侧切线方向一致，形成向内收的凹肩。
4. 折叠细线 `IslandClip` 改为 `Rect="0,0,200,2"`，`IslandHairline` 的 `Margin` 改为 `10,0,10,0`，与更窄的顶边对齐；热区 200x46。
5. 岛内 5 个按钮由 26x26/FontSize 14 缩为 22x22/FontSize 12，时钟 40→34 宽、10→9 号字，总宽 144 落在窄肩内宽 152 中，整体更精致。
6. 展开动画继续由 `IslandRoot.Width/Height` 驱动，未改动画机制。

**实现方式**：`MainWindow.xaml`。

```xml
<Path x:Name="IslandShape" Stretch="Fill"
      Data="M 10,0 H 190 C 182.2,0 176,5.4 176,12 V 22 C 176,28.6 169.8,34 164,34 H 36 C 30.2,34 24,28.6 24,22 V 12 C 24,5.4 17.8,0 10,0 Z"
      Fill="#F2353537" Stroke="#FFFFFF" StrokeThickness="1" />
<Rectangle x:Name="IslandHairline" Height="2" Margin="10,0,10,0"
           VerticalAlignment="Top" RadiusX="1" RadiusY="1" Fill="#FFFFFF" />
```

**验证**：路径左右控制点关于 `x=100` 严格对称；`dotnet build -c Release` 通过。禁止回退到 R49 的 `C 228,8.8 220.8,16 212,16` 右肩写法。

---

### R54 窗口适配图片以图片屏幕位置为锚点

**需求原文摘要**：优化按图片大小调整窗口的位置匹配逻辑；当前尺寸正确但位置以窗口左上角为基点，逻辑错乱；要求以图片为固定位置，把窗口适配过来。

**实现状态**：已实现。

**实现逻辑**：
1. 新增 `Win32.ClientToScreen`，把窗口客户区原点换算为物理屏幕坐标，并得到客户区相对外框的插入量 `clientPadX/clientPadY`。
2. 改变尺寸前先记录图片当前左上角的物理屏幕坐标：`clientOrigin + Canvas.GetLeft/Top(ImageView) * DpiScale`。
3. 目标窗口位置由图片锚点反推：`left = imageLeftScreen - clientPadX`、`top = imageTopScreen - clientPadY`，再按工作区做边界钳制；不再使用 `rect.Left/rect.Top`。
4. 若被工作区钳制，残差写入 `_offsetX/_offsetY`，在异步布局回调中应用，使图片仍尽量停在原屏幕位置。
5. 回调中不再无条件把偏移清零，而是用捕获的 `residualOffsetX/Y` 配合最终客户区重算 `_zoom`；`_fittingWindow` 继续抑制 `SizeChanged` 引起的重入。
6. 工作区超限时的等比缩小、WindowChrome 隐形边框差值计算保持 R47 逻辑不变。

**实现方式**：`Native/Win32.cs`、`MainWindow.xaml.cs`。

```csharp
var clientOrigin = new Win32.POINT(0, 0);
if (!Win32.ClientToScreen(hwnd, ref clientOrigin)) return;
var clientPadX = clientOrigin.X - rect.Left;
var clientPadY = clientOrigin.Y - rect.Top;

var imageLeftDip = double.IsNaN(Canvas.GetLeft(ImageView)) ? 0 : Canvas.GetLeft(ImageView);
var imageTopDip = double.IsNaN(Canvas.GetTop(ImageView)) ? 0 : Canvas.GetTop(ImageView);
var imageLeftScreen = clientOrigin.X + (int)Math.Round(imageLeftDip * dpi.DpiScaleX);
var imageTopScreen = clientOrigin.Y + (int)Math.Round(imageTopDip * dpi.DpiScaleY);

var left = Math.Clamp(imageLeftScreen - clientPadX, area.Left, Math.Max(area.Left, area.Right - width));
var top = Math.Clamp(imageTopScreen - clientPadY, area.Top, Math.Max(area.Top, area.Bottom - height));
var residualOffsetX = (imageLeftScreen - (left + clientPadX)) / dpi.DpiScaleX;
var residualOffsetY = (imageTopScreen - (top + clientPadY)) / dpi.DpiScaleY;
```

**验证**：`dotnet build -c Release` 通过；执行 `Shift+Ctrl+Alt+R` 时窗口围绕图片原屏幕位置收缩/扩张，图片四边仍与画布重合，不再以窗口左上角为基点跳动。

---

### R55 双向适配按钮的双层形象图标

**需求原文摘要**：优化“以窗口匹配图片大小”和“以图片大小匹配窗口”的图标，希望更形象；logo 设计成两层，以图片为准的上面一层是图片形象，以窗口为准的上面一层是窗口形象。

**实现状态**：已实现。

**实现逻辑**：
1. 废弃原来的 `⤢` / `⤡` 抽象符号，改用两个 `Path` 叠加的双层徽标：底层为来源形状（灰色描边、透明填充），上层为目标形状（岛体色填充 + 白色描边，`Margin=4,4` 形成叠层）。
2. `WindowToImageButton`（窗口适配图片，目标=图片）：底层窗口轮廓，上层图片轮廓（矩形 + 太阳 + 山形折线）。
3. `ImageToWindowButton`（图片适配窗口，目标=窗口）：底层图片轮廓，上层窗口轮廓（矩形 + 标题栏分隔线）。
4. 图标尺寸统一 18x18，图层 14x14，放在 22x22 按钮内；ToolTip 同时明确“以图片大小为准/以窗口大小为准”。

**实现方式**：`MainWindow.xaml`。

```xml
<Button x:Name="WindowToImageButton" ... ToolTip="窗口适配图片：以图片大小为准（Shift+Ctrl+Alt+R）">
    <Grid Width="18" Height="18">
        <Path Style="{StaticResource LayerIcon}"
              Data="M1,2.5 H13 V11.5 H1 Z M1,5 H13" Stroke="#FF7A8290" Fill="Transparent" />
        <Path Style="{StaticResource LayerIcon}" Margin="4,4,0,0"
              Data="M1,2.5 H13 V11.5 H1 Z M10,4.7 H11.6 V6.3 H10 Z M1.8,10.6 L5.2,7.2 L7.8,9.6 L10.4,6.6 L13.2,10.8"
              Stroke="#FFFFFF" Fill="#F2353537" />
    </Grid>
</Button>
```

**验证**：`dotnet build -c Release` 通过；两个图标的上下层与功能目标一一对应。

---

### R56 置顶/关闭按钮顶部齐平并按灵动岛方式构建外框

**需求原文摘要**：优化置顶和关闭两个按钮，两个按钮上面与窗口顶部重合，并采用灵动岛的外形构建方式构建两个按钮的外框。

**实现状态**：已实现。

**实现逻辑**：
1. `CloseHost` 高度由 40 收到 28，宽度 64→58（30+28）；按钮去掉顶部 `Margin`，`CloseStrip` 展开目标由 `Y=2` 改为 `Y=0`，收起目标 `-32` 改为 `-30`，保证展开后按钮顶边与窗口顶边严格齐平。
2. 两个按钮的外框由 `Border + CornerRadius` 换成 `Path`，使用与灵动岛相同的构造法：顶边 `M 2,0 H 28`（或 26）与窗口相切，左右肩为互镜像的凹肩贝塞尔，两侧竖直，底部用圆角收口。
3. 图钉状态由 `DataTrigger` 改为设置 `Path.Fill`：钉入为琥珀亮色，拔起为暗灰 + 图钉 42% 透明并倾斜 35°；关闭按钮悬停时外框填充变亮红。
4. 图标文字保留 `TextBlock` / `ContentPresenter`，绘制在 `Path` 之上。

**实现方式**：`MainWindow.xaml`、`MainWindow.xaml.cs`。

```xml
<Grid x:Name="CloseHost" Width="58" Height="28" HorizontalAlignment="Right"
      VerticalAlignment="Top" Background="Transparent" ClipToBounds="True">
    <StackPanel x:Name="CloseStrip" HorizontalAlignment="Right" VerticalAlignment="Top">
        <StackPanel.RenderTransform>
            <TranslateTransform x:Name="CloseTransform" Y="-30" />
        </StackPanel.RenderTransform>
        ...
    </StackPanel>
</Grid>
```

```xml
<Path x:Name="PinShape" Stretch="Fill"
      Data="M 2,0 H 28 C 26.2,0 24,1.8 24,4 V 20 C 24,23.3 21.3,26 18,26 H 12 C 8.7,26 6,23.3 6,20 V 4 C 6,1.8 3.8,0 2,0 Z"
      Fill="#FFD99A2B" Stroke="#55FFFFFF" StrokeThickness="0.8" />
```

```csharp
CloseHost.MouseEnter += (_, _) => AnimateCloseButton(0);
CloseHost.MouseLeave += (_, _) => AnimateCloseButton(-30);
```

**验证**：`dotnet build -c Release` 通过；路径左右控制点关于按钮中线对称。

---

### R57 常规 Windows exe 安装程序

**需求原文摘要**：优化安装包构建方式，希望像正常软件一样生成一个 exe 安装程序，运行后把项目安装到 Windows 系统并作为图片默认打开方式。

**实现状态**：已实现；默认应用的最后一步仍按 Windows 11 规则由用户在系统对话框中确认。

**实现逻辑**：
1. 新增 `src/WBInstaller/` WinForms 项目，发布为单文件 `GuaishushuViewerSetup.exe`；内嵌发布后的 `GuaishushuViewer.exe`（`LogicalName=GuaishushuViewer.exe`），因此安装程序自带主程序。
2. 安装界面显示版本、目标目录、进度与状态；点击安装后解压内嵌 exe 到 `%LOCALAPPDATA%\GuaishushuViewer`。
3. 注册 HKCU ProgID、DefaultIcon、shell\open\command；对所有支持格式写 `Software\Classes\<ext>`、`OpenWithProgids` 和 `ApplicationAssociationToasts`，但不伪造受保护的 `UserChoice`。
4. 通过 `WScript.Shell` COM 创建桌面与开始菜单快捷方式，写入卸载注册表项（`DisplayVersion` 来自发布时传入的 `-p:Version`，无版本硬编码），调用 `SHChangeNotify` 刷新 Shell。
5. 完成后启动 `GuaishushuViewer.exe --set-default`，打开 Windows 自带“打开方式”窗口；用户选择“怪叔叔看图”并勾选始终使用即完成默认关联。
6. `封装安装包.bat` 先发布主程序到 `artifacts/setup-stage/`，再发布安装程序到 `artifacts/installer-stage/`，最终输出 `artifacts/setup/GuaishushuViewer-v<Version>-Setup.exe` 与固定名副本；原 ZIP + `install.cmd` 保留为兼容方式。

**实现方式**：`src/WBInstaller/WBInstaller.csproj`、`src/WBInstaller/Program.cs`、`封装安装包.bat`。

```xml
<EmbeddedResource Include="..\..\artifacts\setup-stage\GuaishushuViewer.exe"
                  LogicalName="GuaishushuViewer.exe"
                  Condition="Exists('..\..\artifacts\setup-stage\GuaishushuViewer.exe')" />
```

```bat
dotnet publish "%SETUP_PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:Version=%VERSION% -o "%INSTALLER_STAGE%"
copy /Y "%INSTALLER_STAGE%\GuaishushuViewerSetup.exe" "%FINAL_EXE%" >nul
```

**验证**：`封装安装包.bat --ci` 实跑通过，生成 `GuaishushuViewer-v1.4.0-Setup.exe`，`ProductVersion` 为干净的 `1.4.0`；安装逻辑覆盖程序、快捷方式、关联、卸载项与默认应用确认。

---

### R58 扩展图片格式与流程图/脑图预览

**需求原文摘要**：写清楚支持哪些图片格式；常用 jpg 系列、png 系列、bmp 系列、icon 系列、web 系列之外希望支持更多常用格式，甚至直接浏览流程图、脑图等格式。

**实现状态**：已实现。

**实现逻辑**：
1. 新建 `Models/SupportedFormats.cs` 作为格式清单唯一来源，分 `Raster`（WIC 直接解码）与 `RichPreview`（Windows Shell 缩略图）两组。
2. `ImageFile.IsSupported`、`FileAssociationService` 与安装程序（链接同一源文件）都从 `SupportedFormats.All` 读取，避免三处漂移。
3. `MainWindow.DecodeImage` 先尝试 `BitmapImage` 原生解码；失败则调用 `ShellPreviewService.TryLoad`，通过 `IShellItemImageFactory` 请求系统缩略图，失败时退而请求关联图标。
4. 解码结果统一转 `Bgra32` 后写入既有 `_sourcePixels`，后续缩放、平移、增强管线完全不变。
5. 关于窗口新增“支持的格式”段落，显示 `SupportedFormats.DisplayText`；README 同步说明“矢量/文档格式依赖系统已安装对应软件生成缩略图，查看的是系统预览位图”。

**实现方式**：`Models/SupportedFormats.cs`、`Services/ShellPreviewService.cs`、`Services/SettingsService.cs`、`Services/FileAssociationService.cs`、`MainWindow.xaml.cs`、`AboutWindow.xaml(.cs)`、`packaging/install.cmd`、`README.md`。

```csharp
private static BitmapSource? DecodeImage(string path)
{
    try
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        return bitmap;
    }
    catch
    {
        return ShellPreviewService.TryLoad(path);
    }
}
```

```csharp
var hr = factory.GetImage(size,
    SigbFlags.BiggerSizeOk | SigbFlags.ThumbnailOnly | SigbFlags.ScaleUp, out bitmap);
if (hr != 0 || bitmap == IntPtr.Zero)
    hr = factory.GetImage(size, SigbFlags.BiggerSizeOk | SigbFlags.IconOnly | SigbFlags.ScaleUp, out bitmap);
```

**验证**：`dotnet build -c Release` 通过；WIC 支持的格式走原生路径，SVG/PDF/XMind/draw.io/Visio 等走 Shell 缩略图路径，无第三方依赖。


---

### R59 封装产物收敛：单文件一个 exe，安装包一个 exe

**需求原文摘要**：为什么执行“封装单文件运行包.bat”后会生成 `GuaishushuViewer.exe` 和 `GuaishushuViewer-v1.3.0.exe` 两个文件？正确方式是只生成一个软件名称加版本号的 exe；安装包批处理只封装产出安装包。

**实现状态**：已实现。

**实现逻辑**：
1. 单文件脚本先把 `dotnet publish` 输出到临时暂存目录 `artifacts/single-stage/`，再把 `GuaishushuViewer.exe` 复制为 `artifacts/single/GuaishushuViewer-v<Version>.exe`，最后删除暂存目录。输出目录每次先整体删除重建，因此不可能残留不带版本号的副本。
2. 安装包脚本删除 ZIP 兼容输出与固定名称副本：`artifacts/setup/` 每次重建后只复制 `GuaishushuViewer-v<Version>-Setup.exe`。主程序仍临时发布到 `artifacts/setup-stage/` 供安装程序内嵌，安装程序发布到 `artifacts/installer-stage/`，两者在脚本结束前删除。
3. 两个脚本仍是固定代码，不写死版本号；版本号继续从 `src/WBImageViewer/WBImageViewer.csproj` 的 `<Version>` 读取。
4. `packaging/install.cmd` 作为源码保留，但不再由“封装安装包.bat”产出到 `artifacts/`。

**实现方式**：`封装单文件运行包.bat`、`封装安装包.bat`。

```bat
set "STAGE=%BUILD%\single-stage"
set "OUT=%BUILD%\single"
if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%OUT%" rd /s /q "%OUT%"
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
copy /Y "%STAGE%\GuaishushuViewer.exe" "%OUT%\GuaishushuViewer-v%VERSION%.exe" >nul
if exist "%STAGE%" rd /s /q "%STAGE%"
```

```bat
rem 安装包脚本只保留这一行产物复制，不再生成 ZIP 或固定名称副本。
copy /Y "%INSTALLER_STAGE%\GuaishushuViewerSetup.exe" "%FINAL_EXE%" >nul
```

**验证**：两个 bat 均以 `--ci` 实跑通过。`artifacts/` 下只有 `single/GuaishushuViewer-v1.5.0.exe` 与 `setup/GuaishushuViewer-v1.5.0-Setup.exe`；两个 exe 的 `ProductVersion` 均为 `1.5.0`。

---

### R60 置顶与关闭合并到同一个岛形面板

**需求原文摘要**：置顶和关闭两个按钮要放在同一个面板上，在这个面板上放置着两个按钮。

**实现状态**：已实现。

**实现逻辑**：
1. 旧的 `CloseStrip` 是 `StackPanel`，两个 `Button` 各自携带一个 `Path` 外框，视觉上是两个独立小岛。
2. 新结构为 `CloseHost → CloseStrip → ActionPanel`：`ActionPanel` 只有一个 `Path` 外框，沿用灵动岛构造法（顶边相切、左右凹肩镜像、底部圆角收口），62×30。
3. `ActionPanel` 内部用两列 `Grid` 放置 `PinButton`（31 宽）与 `CloseButton`（27 宽），中间一条 1px 半透明分隔线；两个按钮本身用透明模板，不再各自画外框。
4. 悬停展开仍由 `CloseHost.MouseEnter/MouseLeave` 驱动 `CloseTransform`，展开目标 `Y=0`、收起 `Y=-32`。

**实现方式**：`MainWindow.xaml`、`MainWindow.xaml.cs`。

```xml
<Grid x:Name="ActionPanel" Width="62" Height="30">
    <Path x:Name="ActionPanelShape" Stretch="Fill"
          Data="M 2,0 H 60 C 58.2,0 56,1.8 56,4 V 22 C 56,26.4 52.4,30 48,30 H 14 C 9.6,30 6,26.4 6,22 V 4 C 6,1.8 3.8,0 2,0 Z"
          Stroke="#66FFFFFF" StrokeThickness="0.8" />
    <Grid Margin="2,0,2,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="31" />
            <ColumnDefinition Width="27" />
        </Grid.ColumnDefinitions>
        <Button x:Name="PinButton" Grid.Column="0" ... />
        <Button x:Name="CloseButton" Grid.Column="1" ... />
    </Grid>
</Grid>
```

**验证**：`dotnet build -c Release` 零错误零警告；展开时两块按钮共用一块连续底板，顶边仍与窗口顶边齐平。

---

### R61 图钉钉入/拔出的动态效果

**需求原文摘要**：优化置顶按钮的动态效果，要明显体现激活置顶时图钉为钉入状态、退出置顶时图钉为拔出状态。

**实现状态**：已实现。

**实现逻辑**：
1. 图钉不再使用 Emoji，改为矢量图形：上半部 `Ellipse` 为钉帽，下半部 `Path` 为钉针，颜色可独立动画。
2. `PinGlyph` 的 `RenderTransform` 用 `TransformGroup` 组合 `RotateTransform Angle` 与 `TranslateTransform Y`；`UpdatePinState()` 根据 `Topmost` 启动 `DoubleAnimation`：
   - 钉入（置顶）：角度 0°、Y=0、不透明度 1、光晕 0.55、琥珀色、底板变暖色。
   - 拔出（取消置顶）：角度 -42°、Y=-5、不透明度 0.4、光晕 0、冷灰色、底板回暗色。
3. 角度与位移使用 `BackEase`（`Amplitude=0.35`）产生轻微过冲，模拟按压/拔起的弹性；不透明度使用 `QuadraticEase`。
4. 面板底色的冷暖切换由 `ActionPanelShape.Style` 中的 `DataTrigger` 绑定 `PinButton.Tag` 实现；图钉颜色的冷暖切换由按钮模板内的 `DataTrigger` 实现；动画只负责位姿与透明度，互不覆盖。
5. 快捷键、按钮点击、托盘菜单都走 `ExecuteHotkey(ToggleTopMost)`，状态源仍是唯一的 `Topmost`。

**实现方式**：`MainWindow.xaml`、`MainWindow.xaml.cs`。

> 注：以下为 R50 时期的自绘图钉历史片段，外形与动画参数已由 R78（恢复 📌 emoji）取代，保留仅作演进记录。

```xml
<Grid x:Name="PinGlyph" RenderTransformOrigin="0.5,0.5">
    <Grid.RenderTransform>
        <TransformGroup>
            <RotateTransform x:Name="PinRotate" Angle="0" />
            <TranslateTransform x:Name="PinLift" Y="0" />
        </TransformGroup>
    </Grid.RenderTransform>
    <Ellipse x:Name="PinHead" Width="10" Height="10" Fill="#FFFFD166" />
    <Path x:Name="PinNeedle" Data="M 9,9 L 9,21" Stroke="#FFFFD166" StrokeThickness="2.4" />
</Grid>
```

```csharp
rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(
    pinned ? 0 : -42, duration)
{
    EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
});
lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
    pinned ? 0 : -5, duration)
{
    EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
});
glyph.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(
    pinned ? 1 : 0.4, duration));
```

**验证**：`dotnet build -c Release` 通过；切换置顶时图钉同时发生旋转、抬起、变暗、光晕消失，取消置顶时反向复位。

---

### R62 常规 Windows 五步安装向导

**需求原文摘要**：安装包执行效果太偏向 AI 大模型风格，希望以普通常规软件安装界面的方式构建安装包和安装过程。

**实现状态**：已实现。

**实现逻辑**：
1. `src/WBInstaller/Program.cs` 从单页卡片式 `InstallForm` 改为 `InstallWizard`：固定对话框尺寸 540×392，蓝色标题横幅、白色内容区、灰色底部按钮栏，使用系统标准字体与 `SystemColors`。
2. 安装流程拆成五步：`欢迎使用` → `许可协议` → `选择目标位置` → `正在安装` → `安装完成`。许可页内嵌根目录 `LICENSE`（MIT 全文）并强制勾选“我接受”后才能继续；目录页支持 `FolderBrowserDialog` 修改安装位置；进度页显示阶段标题、说明和进度条；完成页提供“运行程序”和“打开默认应用确认窗口”两个勾选项。
3. 安装核心逻辑不变：内嵌主程序释放到目标目录、注册 ProgID/OpenWith、创建桌面与开始菜单快捷方式、写入当前用户卸载项、调用 `SHChangeNotify` 刷新 Shell。
4. `WBInstaller.csproj` 新增 `<EmbeddedResource Include="..\..\LICENSE" LogicalName="LICENSE.txt" />`，安装向导从程序集资源读取许可文本，不依赖外部文件。
5. 默认路径仍为 `%LOCALAPPDATA%\GuaishushuViewer`，版本号仍从程序集信息版本派生，安装向导标题显示真实版本。

**实现方式**：`src/WBInstaller/Program.cs`、`src/WBInstaller/WBInstaller.csproj`。

```csharp
private readonly Panel[] _pages = new Panel[5];   // Welcome / License / Destination / Installing / Finished
private void ShowPage(int page)
{
    _page = page;
    foreach (var item in _pages) item.Visible = false;
    _pages[page].Visible = true;
    RefreshButtons();
}
```

```xml
<EmbeddedResource Include="..\..\LICENSE" LogicalName="LICENSE.txt" />
```

**验证**：`dotnet build -c Release` 与 `封装安装包.bat --ci` 通过；生成的 `GuaishushuViewer-v1.5.0-Setup.exe` 为单文件安装程序，运行后按五步向导执行安装。


---

### R63 重建安装向导：实时校验、分阶段日志与错误详情

**需求原文摘要**：安装执行到第二步没有任何错误提示，但“下一步”灰不可用；需要彻底检查并重建带实时动态检查功能的安装界面，除进度外还要能判断安装错误信息。

**实现状态**：已实现。

**根因**：R62 的许可页把“我接受许可协议”复选框放在 `y=268`，而内容区实际可用高度只有约 222px，复选框被裁切到可视区域外。用户看不到勾选框，`_next.Enabled = _accept.Checked` 自然永远为 false，且当时没有任何状态文本解释原因。

**实现逻辑**：
1. 许可页改为在 `y=298` 放置复选框，并把窗体高度从 520 提升到 520 的可用内容区（标题 82 + 底部 62，内容区 376），复选框与协议框完整可见。
2. 底部新增常驻状态栏：彩色圆点 + 状态文字，分信息/通过/警告/错误四态。许可页未勾选时显示橙色警告“请先阅读并勾选…”；勾选后立即变绿并启用“下一步”。
3. 目标位置页新增 `ValidateDestination()`：每 900ms 由 `Timer` 实时复查一次，检查空路径、磁盘根目录、目录可创建、探针文件可写、剩余空间 ≥10MB、是否已有安装；结果同时写入页内提示与底部状态栏，并决定“安装”按钮是否可用。
4. 安装过程改为事件驱动：`InstallCore` 按“创建目录 → 释放主程序 → 注册 ProgID → 注册关联 → 创建快捷方式 → 写入卸载信息”分阶段执行，每阶段通过 `IProgress<InstallEvent>` 上报开始、进度、完成或错误。
5. 每个阶段由 `Stage()` 包裹 `try/catch`；失败时上报异常类型与消息，进度页保留在失败界面，写入红色“错误详情”文本框，“下一步”变为“重试”，并允许“上一步”返回修改目录，不再静默失败。
6. 成功后自动进入完成页；完成页启动程序或默认应用确认失败时也会捕获并通过状态栏与消息框报告。

**实现方式**：`src/WBInstaller/Program.cs`。

```csharp
private static bool Stage(IProgress<InstallEvent> progress, int value, string name, Action action)
{
    progress.Report(new InstallEvent(InstallEventKind.Log, value, $"开始：{name}"));
    try
    {
        action();
        progress.Report(new InstallEvent(InstallEventKind.Progress, value, name));
        progress.Report(new InstallEvent(InstallEventKind.Log, value, $"完成：{name}"));
        return true;
    }
    catch (Exception ex)
    {
        progress.Report(new InstallEvent(InstallEventKind.Error, value,
            $"{name}失败：{ex.GetType().Name}: {ex.Message}"));
        return false;
    }
}
```

```csharp
_checkTimer.Tick += (_, _) => { if (_page == 2 && !_installing) ValidateDestination(); };
_checkTimer.Start();
```

**验证**：`dotnet build -c Release` 通过；安装 exe 启动后标题栏正常，许可页复选框位于可视区，未勾选时底部显示橙色警告且下一步禁用，勾选后变绿并启用。安装失败路径会保留在进度页并显示异常详情与“重试”。

---

### R64 安装界面布局重排

**需求原文摘要**：优化安装界面，让布局更合理、美观。

**实现状态**：已实现。

**实现逻辑**：
1. 窗体尺寸改为 700×520，分为蓝色品牌头（82px）、左右分栏主体与灰色底栏（62px）。
2. 头部放置 46×46 的新 Logo、产品名、版本与副标题。
3. 主体左侧 172px 为浅灰步骤导航栏，使用 ①–⑤ 标记五个步骤，当前步骤高亮蓝底蓝字加粗；右侧白色内容区按页绘制标题、提示与控件。
4. 底部状态栏左侧显示实时状态，右侧依次排列“上一步 / 下一步 / 取消”，尺寸与间距统一。
5. 进度页把当前步骤、进度条、时间戳日志、红底错误详情框按垂直顺序排列，避免信息重叠。
6. WinForms 控件统一使用 Microsoft YaHei UI 9F，标题 14F 加粗，颜色使用系统灰蓝体系，保持常规 Windows 安装程序观感。

**实现方式**：`src/WBInstaller/Program.cs` 的 `BuildHeader()`、`BuildBody()`、`BuildFooter()`、`BuildPages()`。

```csharp
string[] names = { "① 欢迎使用", "② 许可协议", "③ 目标位置", "④ 安装进度", "⑤ 安装完成" };
for (var i = 0; i < names.Length; i++)
{
    var label = new Label { Text = names[i], Location = new Point(0, 26 + i * 54), Size = new Size(172, 42), ... };
    _stepLabels[i] = label;
    _side.Controls.Add(label);
}
```

**验证**：安装程序实启动，窗口标题为“怪叔叔看图 安装程序”，主窗口句柄正常创建；各页控件坐标均在内容区内，无裁切。

---

### R65 主题 Logo 与全场景应用

**需求原文摘要**：软件 Logo 太丑；希望是与软件名称元素相关、个性主题突出且简洁的形象；并且全面应用到任务栏、状态栏、任务管理器等位置。

**实现状态**：已实现。

**实现逻辑**：
1. 用纯 Python 标准库（`struct`/`zlib`）栅格化 768×768 超采样画布并降采样，重新设计 Logo：深色圆角底 + 米白照片框 + 戴眼镜眨眼的大叔 + 琥珀色图钉。图钉呼应“置顶”，照片框与人物呼应“怪叔叔看图”，整体在小尺寸仍能辨认。
2. 输出 `Assets/app.ico`，包含 16/24/32/48/64/128/256 七种尺寸的 PNG 压缩 ICO；同时输出 `Assets/app.png` 供界面内展示。
3. 查看器：`<ApplicationIcon>` 指定新 ico，Win32 可执行文件图标、资源管理器、任务栏与任务管理器一致；`MainWindow`、`SettingsWindow`、`AboutWindow` 均设置 `Icon="Assets/app.ico"`；关于窗口把原“奇”字占位块换成 `app.png`。
4. 托盘：`App.LoadTrayIcon()` 继续从 `Assets/app.ico` 资源加载，因此通知区域图标同步更新。
5. 文件关联与快捷方式：注册表 `DefaultIcon` 与 `.lnk` 的 `IconLocation` 都指向主 exe 的 0 号图标，自动使用新 Logo。
6. 安装程序：`WBInstaller.csproj` 同时把新 ico 与 png 作为嵌入资源，`ApplicationIcon` 使用新 ico；`InstallWizard.LoadBrandIcons()` 把 ico 设为窗体 `Icon`（任务栏/标题栏），把 png 设为头部 PictureBox。

**实现方式**：`src/WBImageViewer/Assets/app.ico`、`Assets/app.png`、三个 WPF 窗口、`App.xaml.cs`、`WBImageViewer.csproj`、`src/WBInstaller/Program.cs`、`WBInstaller.csproj`。

```xml
<ApplicationIcon>Assets\app.ico</ApplicationIcon>
<Resource Include="Assets\app.png" />
```

```xml
<EmbeddedResource Include="..\WBImageViewer\Assets\app.ico" LogicalName="AppIcon.ico" />
<EmbeddedResource Include="..\WBImageViewer\Assets\app.png" LogicalName="AppLogo.png" />
```

**验证**：新 PNG 已人工目视确认；编译零警告零错误；两个封装脚本 `--ci` 通过，产物版本为 1.6.0；安装程序 exe 启动正常。



---

### R66 修复 1.6.0 单文件无法启动

**需求原文摘要**：1.6.0 版本单文件程序无法运行，打开后没有软件界面，托盘图标也没有显示。

**实现状态**：已修复。

**根因**：R61 把图钉悬停发光写成 `<MultiTrigger>`，并在其中放了带 `Binding` 的 `<Condition>`。WPF 的 `MultiTrigger` 只接受 `Property` 条件，带 Binding 的条件必须放在 `MultiDataTrigger`；加载 `MainWindow.xaml` 的按钮模板时抛出 `XamlParseException → Condition.Seal(): 不能将 Property 为 null 的条件封印`。异常发生在 `App.OpenFiles → new MainWindow()`，所以窗口和托盘都没有创建。此前的 `dotnet build` 只做 XAML 编译，不执行模板封印，因此编译阶段没有暴露。

**实现方式**：`MainWindow.xaml`。

```xml
<!-- 错误写法 -->
<MultiTrigger>
  <MultiTrigger.Conditions>
    <Condition Property="IsMouseOver" Value="True" />
    <Condition Binding="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Value="Pinned" />
  </MultiTrigger.Conditions>
</MultiTrigger>

<!-- 正确写法 -->
<MultiDataTrigger>
  <MultiDataTrigger.Conditions>
    <Condition Binding="{Binding IsMouseOver, RelativeSource={RelativeSource TemplatedParent}}" Value="True" />
    <Condition Binding="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Value="Pinned" />
  </MultiDataTrigger.Conditions>
  <Setter TargetName="PinHalo" Property="Opacity" Value="0.85" />
</MultiDataTrigger>
```

**验证**：`dotnet build -c Release` 后直接运行 `GuaishushuViewer.dll` 无任何未处理异常；启动进程 `MainWindowTitle` 为“怪叔叔看图”，窗口与托盘均正常创建。

---

### R67 安装主体改用 TableLayoutPanel

**需求原文摘要**：安装界面左边构建了步骤显示区，但没有兼顾整体，导致右边内容区被覆盖；要求调整布局逻辑，不要再犯低级错误。

**实现状态**：已修复。

**根因**：R64 在同一个 `Panel` 里先 `Controls.Add(_side)`（`Dock=Left`），再 `Controls.Add(_content)`（`Dock=Fill`）。WinForms 的 Dock 按 z-order 从后往前生效，后加入的 Fill 控件先占满整个父容器，Left 控件随后覆盖在它左侧，导致内容区被步骤区遮挡。

**实现方式**：`src/WBInstaller/Program.cs` 的 `BuildBody()` 改为显式两列表格，不再依赖 Dock 顺序。

```csharp
var table = new TableLayoutPanel
{
    Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
    BackColor = Color.White, Margin = Padding.Empty, Padding = Padding.Empty
};
table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));
table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
_side.Dock = DockStyle.Fill;
_content.Dock = DockStyle.Fill;
table.Controls.Add(_side, 0, 0);
table.Controls.Add(_content, 1, 0);
_body.Controls.Add(table);
```

**验证**：安装程序实启动窗口标题正常；左栏 172px、右栏占满剩余宽度，两个区域在布局层面互斥，不可能重叠。

---

### R68 静默默认关联与图片右键菜单

**需求原文摘要**：安装后应自动关联成为图片默认打开程序，而不是弹出 Windows 程序管理设置窗口；另外在图片文件右键加入“用本软件打开”菜单。

**实现状态**：已实现。

**实现逻辑**：
1. 新建 `Services/DefaultAssociationService.cs` 作为唯一关联入口，查看器与安装程序共用（通过 `<Compile Include>` 链接到安装程序）。
2. Windows 8+ 的默认选择保存在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\<ext>\UserChoice`，由 `ProgId` + `Hash` 双值构成；只写 `OpenWithProgids` 只能成为候选。新服务按社区逆向出的 shell 算法计算哈希：构造 `extension + SID + progId + 注册表键写入时间(钳制到分钟) + 固定 experience 字符串 + \0`，UTF-16LE 小写后取 MD5，再经两组 32 位乘加混合 `Sub1/Sub2` 异或，Base64 写入 `Hash`。
3. `ApplyAll()` 同时完成：注册 ProgID/DefaultIcon/open command、扩展名默认值与 OpenWithProgids、UserChoice 哈希、右键动词、ApplicationAssociationToasts 抑制提示，并刷新 Shell。
4. 右键菜单写入 `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\GuaishushuViewer.Open`，标题“用怪叔叔看图打开”，带本程序图标与命令行；即使当前默认程序是别的软件也会显示。
5. 安装向导的“注册 ProgID / 注册关联候选”两阶段合并为“设为默认打开程序并注入右键菜单”；任何格式失败都会把具体错误写进安装日志并让安装失败，可重试。完成页不再出现“打开默认应用确认窗口”选项，`--set-default` 不再调用 `OpenChooseAppDialog()`。
6. 设置窗口的按钮改为“立即应用默认关联”，提示文案说明使用 UserChoice 哈希且失败会提示。

**实现方式**：`src/WBImageViewer/Services/DefaultAssociationService.cs`、`App.xaml.cs`、`FileAssociationService.cs`、`SettingsWindow.xaml(.cs)`、`src/WBInstaller/Program.cs`、`WBInstaller.csproj`。

```csharp
var source = (normalized + sid + progId + regDate + Experience + "\0").ToLowerInvariant();
var bytes = Encoding.Unicode.GetBytes(source);
var md5 = MD5.HashData(bytes);
var hash = Convert.ToBase64String(XorBytes(Sub1(bytes, md5), Sub2(bytes, md5)));
key.SetValue("ProgId", progId);
key.SetValue("Hash", hash);
```

```csharp
using var verb = Registry.CurrentUser.CreateSubKey(
    $@"Software\Classes\SystemFileAssociations\{extension}\shell\GuaishushuViewer.Open");
verb.SetValue(string.Empty, "用怪叔叔看图打开");
verb.SetValue("Icon", $"\"{executablePath},0\"");
using var command = verb.CreateSubKey("command");
command.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
```

**验证**：两个项目均编译通过；`--set-default` 与安装程序共用同一入口，错误通过安装日志/MessageBox 明确报告。该算法属于未公开机制，档案中保留“系统更新可能改变算法”的风险说明。

---

### R69 图片标注与罗盘菜单

**需求原文摘要**：增加直线、波浪线、双线、自由画线、手绘圈注、橡皮擦（自由擦除、点击擦除元素、调节笔触大小、全部清除）等标注；用类似 SolidWorks 鼠标罗盘的方式管理颜色/擦除方式/笔触；默认 `Ctrl + 鼠标右键` 拖动触发，拖动时显示从起点跟随鼠标的指示线；快捷键可在后台设置。

**实现状态**：已实现。

**实现逻辑**：
1. 新增 `Models/AnnotationModels.cs`：`AnnotationTool` 枚举与 `AnnotationStroke`。笔迹点以**图片归一化坐标 0..1** 保存，`CreateVisual(toViewport)` 与 `HitTest` 在渲染/命中时换算到视口坐标，因此缩放、平移、窗口尺寸变化后笔迹始终贴合图片。
2. 波浪线按两点间正弦采样生成；双线按法线偏移生成两条平行线；圈注按拖拽包围盒生成带轻微抖动的闭合椭圆；自由画保留采样点。
3. 橡皮擦：`EraserFree` 拖动时逐段命中并删除笔迹，显示虚线圆形笔触预览；`EraserClick` 单击删除命中的整条笔迹；“大小”扇区循环笔触与橡皮尺寸；“清空”扇区删除全部笔迹。
4. `MainWindow.xaml` 在图片画布之上加入 `AnnotationLayer` 与 `CompassLayer` 两个 `IsHitTestVisible=False` 的 Canvas；左下角 `ToolBadge` 常驻显示当前工具和笔触大小。
5. 罗盘为 12 个 30° 扇区（指针/直线/波浪/双线/自由/圈注/擦除/点擦/大小/红/换色/清空），用 `StreamGeometry` 绘制内外半径 36/94 的扇环；按下点为中心，拖动时 `Line` 虚线从起点连到光标，当前扇区提亮加粗；松手触发对应动作。
6. 触发条件由 `AppSettings.CompassModifier`（Ctrl/Shift/Alt/组合）与 `CompassButton`（右键/中键）决定，默认 `Ctrl + 右键`；后台设置页新增两个下拉框。`OnPreviewMouseDown/Move/Up` 优先处理罗盘与标注，避免与原有窗口拖动、图片平移冲突。

**实现方式**：`src/WBImageViewer/Models/AnnotationModels.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`Models/AppSettings.cs`、`SettingsWindow.xaml(.cs)`。

```csharp
private bool IsCompassTrigger(MouseButtonEventArgs e) =>
    e.ChangedButton == CompassMouseButton && Keyboard.Modifiers == CompassModifiers;
```

```csharp
var source = (normalized + sid + progId + regDate + Experience + "\0").ToLowerInvariant();
```

```csharp
_annotations.Add(new AnnotationStroke(_annotationTool, _draftPoints,
    _annotationColor, _annotationSize));
RedrawAnnotations();
```

**验证**：编译 0 警告 0 错误；启动后无 XAML 异常；罗盘扇区左右对称覆盖 360°，指示线跟随光标，松手触发；笔迹在 `ApplyImagePosition()` 中随图片重绘。

### R70 安装向导根布局改为三行 TableLayoutPanel（修复顶部 Logo 覆盖内容）

- **问题**：安装进度/错误页右侧内容被顶部蓝色 Logo 区覆盖，文字与进度条不可见。
- **根因**：`header(Dock.Top)` 先 `Controls.Add`、`body(Dock.Fill)` 后加。WinForms 按 z-order 逆序停靠：后加的 Fill 先占满客户区，先加的 Top 最后停靠并压在 Fill 之上。左栏两列虽已用 TableLayoutPanel，但顶层三块仍是 Dock 混排。
- **修复**：新增根 `_root = new TableLayoutPanel`（1 列 3 行：Header 绝对 82 / Body 百分比 100 / Footer 绝对 62），`header/body/footer` 全部 `Dock.Fill` 放入各自单元格，`Controls.Add(_root)` 只加一次。任何停靠操作都不可能跨单元格覆盖（R70）。
- **验证**：编译 0 警告 0 错误；进度页标题、进度条、日志、错误详情均在 Body 行内完整可见。

### R71 安装时文件关联失败不再中止安装

- **问题**：安装日志显示"设为默认打开程序并注入右键菜单失败：InvalidOperationException: 1 个格式未能完成默认关联"，整个安装被判定失败；实际只是个别扩展名的 UserChoice 哈希被 Windows 保护而回滚。
- **修复**：① 关联阶段改名"注册文件关联与右键菜单"，失败项以 `InstallEventKind.Warning` 写入日志而**不抛异常**；② 新增 `_associationNote`，完成时在完成页显示温和提示；③ 完成页文案改为"注册关联候选"，不再宣称一定成为默认；④ 新增 `Warning` 事件类型；⑤ `SetUserChoice` 对 UserChoice 键"删除失败则改为就地打开写入"兜底，减少受保护键导致的假失败（R71）。
- **原则**：文件关联是 best-effort，绝不能让单个格式失败回滚整个安装；ProgID/OpenWithProgids/右键菜单对所有格式照常写入。

### R72 灵动岛彻底移除时间显示

- 删除 XAML `IslandClock` TextBlock、C# `_clockTimer`、`UpdateIslandClock()` 方法及构造函数中的定时器初始化。灵动岛只承载工具按钮，不再显示时钟（R72）。

### R73 三窗口协同置顶与激活置顶

- **需求**：主窗口 / 关于 / 后台设置三者共享置顶权；任一被激活时升到三者最上层。
- **实现**：新增 `Services/TopmostCoordinator.cs`：静态 `Tracked` 列表 + `TopmostEnabled`；`Register()` 设初始 Topmost 并挂 `Activated → Raise()`、`Closed → 移除`；`SetTopmost(bool)` 一次切换全部；`Raise()` 用 `SetWindowPos(hwnd, HWND_TOPMOST/NONE, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)` 把激活窗口插入 z-order 顶部（关闭置顶时用 `IntPtr.Zero` 即 HWND_TOP）。Win32 新增 `HWND_TOPMOST/HWND_NOTOPMOST` 与 `SWP_NOMOVE` 常量。三窗口构造函数均 `Register(this)`；主窗口置顶快捷键改走 `TopmostCoordinator.SetTopmost()`，图钉状态随之同步（R73）。
- **注意**：不在设置/关于窗口上再写 `Topmost=True` 硬编码，统一由协调器管理，避免状态不一致。

### R74 全局快捷键冲突检测

- **需求**：快捷键与其他软件冲突时无任何提示；希望知道被谁占用。
- **实现**：新增 `Services/HotkeyConflictService.cs`。Windows 无公开 API 查询热键所有者，采用三层检测：① 在专用 message-only HWND 上尝试 `RegisterHotKey`，失败且 `GetLastWin32Error()==1409` 判定为被占用；② 遍历开始菜单/桌面 `.lnk`（`WScript.Shell.Hotkey`）匹配已分配快捷键，给出占用程序名与路径；③ `ReservedCombos` 表列出系统保留组合（任务管理器/安全选项/任务切换等）。结果含状态与疑似占用者。设置页每个快捷键框下方实时显示冲突状态，新增"检测快捷键冲突"按钮，保存时对占用/保留项弹窗确认（不强制阻止）。`HotkeyHost` 新增 `FailedRegistrations`，`App` 启动后托盘气泡提示未能注册的快捷键及疑似占用者（R74）。
- **说明**：若无法从 `.lnk` 找到占用者，明确提示"Windows 不公开占用者名称"，不臆测。

### R75 标注罗盘重构：分层分组菜单 + 文字提示 + 动态指示线

- **需求**：旧 12 扇区平铺罗盘不美观、无文字提示；希望内外多层、按分类逐级展开/收回；跟随线要动态、位于罗盘上层且任意背景下醒目。
- **数据模型**：`CompassNode`（Label/Glyph/Description/Accent/Action/TargetColor/Children）构成树。内层 5 分类：形状、颜色、橡皮、粗细、工具；"颜色"下再分暖/冷/自然/中性 4 色系，每个色系 3 个具体颜色 —— 形成 3 层（分类 → 色系 → 颜色）。橡皮/粗细/工具为 2 层叶子。
- **交互**：`CompassRingInner/Outer` 四组半径；`UpdateCompass()` 按指针距离定 `activeRing`，`maxRing = activeRing+1` 逐层预览（悬停分类即展开子菜单，移回中心圆收回全部）；`HandleCompassUp()` 只对当前环高亮的叶子触发动作，避免误触预览层。
- **视觉**：`RebuildCompass()` 按当前路径重建扇区 Path（高亮加亮+白色描边）、分类层同时印出名称文字；`AddCompassText()` 统一绘制。悬停时 `Border+TextBlock` 提示框显示"名称 — 说明"。
- **指示线**：`OpenCompass()` 最后添加 `_compassGuideHalo`（深色粗底）+ `_compassGuideFlow`（黄青流动的虚线，`ColorAnimation` 黄↔青 + `DoubleAnimation` 虚线偏移 0→-32 循环 + 黄色发光），确保位于所有扇区之上、任意背景醒目；`CloseCompass()` 清理。
- **配套**：`ShowToolBadge()` 边框色改用当前笔迹颜色（R75）。

### R76 罗盘新增“移动标注元素”工具

- **需求**：标注完成后需要调整笔迹位置，罗盘菜单缺少移动功能。
- **枚举与菜单**：`AnnotationTool.Move`、`CompassAction.Move` 加入枚举；“工具”分类新增叶子 `CNode("移动", "✜", "拖动已绘制的标注元素", ...)`；`InvokeCompass()` 调用 `SetAnnotationTool(AnnotationTool.Move)`；`ShowToolBadge()` 显示“标注：移动元素（拖动已有笔迹）”；鼠标光标为 `Cursors.SizeAll`。
- **交互**：`HandleAnnotationDown()` 在 Move 模式下从后往前 `HitTest`（半径 `max(12, 笔宽×2)`），命中则记录 `_movingAnnotation`、`_moveStartImage`（图片归一化坐标）与 `_moveOriginalPoints` 快照并捕获鼠标；未命中则返回 false 让左键继续平移图片。`HandleAnnotationMove()` 用当前归一化坐标减去起点得到位移，逐点写回快照+位移并 `RedrawAnnotations()`，因此缩放、平移、窗口变化后移动依然精确。`HandleAnnotationUp()` 与 `OnLostMouseCapture()` 清理移动状态。`RestoreCursor()` 改为按当前工具恢复光标（移动工具回到 SizeAll）（R76）。

### R77 手绘风格笔迹引擎

- **需求**：按参考图把直线、双线、波浪线与圈注从几何规则线改造为完全模拟手绘的笔触外形。
- **几何**：`AnnotationStroke` 新增基于笔迹点哈希的稳定 `_seed` 与 `Noise(i)`（仅依赖序号，不依赖坐标），所有抖动在视口空间按 `t` 参数生成，缩放/重绘时形状完全稳定。
  - 直线 `SketchLine()`：轻微弓形 + 三次低频摆动 + 微抖动，两端各外溢约 1.2%。
  - 波浪线 `SketchWave()`：两道重叠波浪，振幅随 `t` 漂移，第二道带间距偏移。
  - 双线 `SketchDouble()`：两道各自轻微波动的平行笔触，间距按笔宽计算，不再是尺规直线。
  - 圈注 `SketchCircle()`：两道独立闭合环（各自起点/扫掠角不同、圆心轻微漂移、半径多频波动），保留手绘起笔收笔的“过头”痕迹。
- **渲染**：`CreateVisual()` 对每条中心线生成**可变宽度填充 ribbon**：`CreateSketchStroke()` 按法线左右偏移，宽度 = 笔宽 × 端部收细 × 压力抖动，用 `StreamGeometry` 填充，模拟真实马克笔的粗细变化；所有笔迹（含自由画）统一走该渲染路径（R77）。

### R78 恢复 📌 图钉外形并添加顶入/拔出动画

- **需求**：置顶图钉曾被改成自绘金色圆点+针，用户要求恢复 emoji 📌 外形并加入顶入/拔出动态效果。
- **实现**：`PinGlyph` 由 `Grid(Ellipse+Path)` 改回 `TextBlock Text="📌" FontFamily="Segoe UI Emoji, Segoe UI Symbol" FontSize="16"`，保留 `PinRotate`/`PinLift` 两个变换；触发器改为控制 `PinGlyph.Opacity`（置顶 1 / 取消 0.45）与光晕透明度。`UpdatePinState()` 的 `DoubleAnimation` 显式给出相反状态的 `From` 值：置顶时从 `Y=-9/Angle=-28` 顶入到 `0/0`，取消时从 `0/0` 拔出到 `-9/-28`，`BackEase` 缓动产生轻微回弹（R78）。

### R79 全场景更换龙形 G 品牌 Logo

- **需求**：以用户提供的龙形 G 图片形状作为软件新 Logo，替换所有场景中的旧 Logo（照片框＋眨眼大叔＋琥珀图钉）。
- **素材处理**：对用户图片执行 `matting` 抠图得到透明背景前景（龙首、朱红祥云、米色回纹、鳞片完整保留，白底与投影去除）；按 alpha 边界框裁成正方形并留 16% 边距，LANCZOS 缩放到 256×256。
- **资源**：覆盖 `src/WBImageViewer/Assets/app.png`（256×256 RGBA 透明 PNG）与 `Assets/app.ico`（16/24/32/48/64/128/256 七尺寸）。由于主窗口、设置/关于窗口、托盘、任务栏、资源管理器关联、快捷方式与安装程序统一引用这两个资源，替换后全场景同步生效（R79）。
- **配套**：`AboutWindow.xaml` 中承载 Logo 的底板由深色 `#101216` 改为浅灰 `#F1F3F5`，避免墨绿龙身在深色底上对比不足；README「Logo 与品牌」章节同步新描述。

### R80 罗盘连线精修与纯视觉化

- **需求**：连接鼠标的线更细、更精致；线只作视觉效果不产生触发，触发以鼠标落点为准。
- **实现**：`OpenCompass()` 中 `_compassGuideHalo` 描边由 7 降到 2.2、`_compassGuideFlow` 由 2.6 降到 1.2，虚线由 `9,7` 改为 `4,5`，流光阴影半径 6；`UpdateCompass()` 的扇区命中只由 `_compassPointer` 的角度与距离计算，连线 `Line` 设置 `IsHitTestVisible=false`，不参与任何命中。`HandleCompassUp()` 仅以 `_compassActiveRing`/`_compassSelection` 触发（R80）。

### R81 罗盘三态高亮与外层粘滞

- **需求**：常态统一风格；悬停项高亮、其余降对比；子层沿用同样逻辑；最外层菜单在鼠标漂出范围时保持显示，只有回到里层才收起。
- **实现**：`RebuildCompass()` 计算 `anyActive`，对每个扇区区分 `active`（当前环选中，255 alpha +40 提亮 + 白色 2px 描边）、`breadcrumb`（祖先路径，205 alpha）、`dim`（其余，92 alpha ×0.55 变暗）；`UpdateCompass()` 中指针漂出所有环时仅移动连线不清空 `_compassSelection`（粘滞），指针回到更内环时才截断更深选择（R81）。

### R82 罗盘三套上下文菜单

- **需求**：画布（含无图空白）显示软件常规功能；图片上激活图片管理/涂鸦菜单；涂鸦元素上激活元素编辑菜单。
- **实现**：新增 `CompassContext{Canvas,Image,Element}` 与 `CanvasCompassTree`/`ElementCompassTree`，`ResolveCompassContext()` 先 `HitTestAnyElement()` 命中元素即选为 Element 并 `SelectElement`，否则按 `IsInsideImage` 判 Image，其余 Canvas。`OpenCompass()` 按上下文构建对应树，Hub 文字随上下文显示画布/标注/元素（R82）。

### R83 元素编辑（端点/边界/移动/删除）

- **需求**：线段拖两端顶点改长度、拖本体移动、Delete 删除；圆圈可调长宽；自由画只移动；线型不能改为圆圈/自由画产物。
- **实现**：`AnnotationStroke` 加 `Id/Opacity/Highlighter` 与可写 `Color/Thickness/Tool`，`GetHandles()` 为线段返回起止点、圆圈返回包围盒两角、自由画/填充返回空；`ApplyHandle()` 按索引改写端点或缩放包围盒并 `Reseed()`；`CanChangeTo()` 限制只有线族之间可互转。`MainWindow.Edit.cs` 的 `TryBeginElementEdit()` 命中手柄进入 `_editingElement`，命中本体进入移动；`ApplyElementEdit()` 分发到 stroke/fill/text；`DeleteSelectedElement`/`DuplicateSelectedElement`/`ChangeSelectedLineType` 完成编辑；`OnPreviewKeyDown` 处理 Delete 与 Esc 退出工具（R83）。

### R84 油漆桶填充

- **需求**：罗盘加入油漆桶，给封闭区域填充指定颜色。
- **实现**：新增 `AnnotationFill`：以归一化种子点 + 颜色 + 容差存储，`Build()` 用栈式洪泛在 `_sourcePixels` 上求连通区并生成透明 `WriteableBitmap` 缓存；`AnnotationTool.Fill` + `CompassAction.Fill`，落笔处 `ToImageSpace` 求种子、`Build` 后加入 `_fills` 并由 `RedrawAnnotations()` 铺到图片层（R84）。

### R85 透明度与荧光笔高亮

- **需求**：渐变透明度类 PPT 黄光高亮，既不遮挡背景又凸显笔迹；罗盘面板与选中元素左下角均可设置。
- **实现**：`AnnotationStroke`/`AnnotationFill`/`AnnotationText` 加 `Opacity`，笔迹加 `Highlighter`（宽度 ×2.6、收细关闭）；罗盘「透明」分类提供高亮 0.35 / 半透明 0.55 / 实心 1.0 / 荧光笔切换，`SetOpacity()` 同时写入当前笔与选中元素；左下角徽标 `ToolBadgeOpacity` 单击循环切换。默认 `AppSettings.DefaultOpacity=0.45`、`DefaultPenWidth=3` 由 `ApplyVisualSettings()` 应用（R85）。

### R86 截图钉桌面（仿微信）

- **需求**：仿微信截图，确认后新窗口置顶显示截图，截图即钉在桌面。
- **实现**：新增 `Views.ScreenshotWindow`：全屏透明置顶遮罩，`StreamGeometry` 双子路径实现暗化挖空，左键拖拽框选、尺寸提示、确认/取消工具条；`Confirm_Click` 用 `Graphics.CopyFromScreen` 按虚拟屏 DPI 比例抓取区域为 `BitmapSource`；`App.OpenBitmap()` 以 `MainWindow.ShowCaptured()` 载入并以 `Topmost=true` 新窗口展示（R86）。

### R87 大模型 API：翻译与文字识别

- **需求**：提供大模型 API 接口，截图可直接翻译、手写文字识别；图片上框选区域进行翻译/识别。
- **实现**：新增 `AiService`（OpenAI 兼容 `chat/completions`，支持文本与 `image_url` base64 多模态），`OcrAsync`/`TranslateImageAsync`/`TranslateTextAsync`；`AppSettings` 增 `AiEndpoint/AiKey/AiModel/AiTargetLanguage`，设置页可填并「测试 API 连接」；`RegionAction{Translate,Ocr}` + 罗盘「AI」分类，框选后 `RunRegionActionAsync()` 裁剪区域、调用 API、把结果存为 `AnnotationText` 卡片（可再移动/删除）（R87）。

### R88 .gjpg 可编辑工程格式

- **需求**：保存/另存为能把图片与标注、翻译、识别痕迹一起保存，下次打开自动加载并可继续编辑；市面无现成格式则自定义 .gjpg。
- **实现**：新增 `GjpgService`：UTF-8 JSON 容器，内嵌原图 base64 PNG + `Annotations`/`Fills`/`Texts`/`Adjustments`；`Save()` 序列化，`TryLoad()` 反序列化并重建笔迹/填充（重新洪泛）/文本卡片；`SupportedFormats.ProjectFiles={".gjpg"}` 并入 `All`；`MainWindow.LoadProject()` 恢复全部编辑对象，`App.OpenDocument()`/`LoadInto()` 按扩展名分流，拖拽与 IPC 打开均走正确加载器（R88）。

### R89 工具徽标交互与快捷设置

- **需求**：左下角提示显示线名与直径；单击线名切换线段形状，点击直径/颜色快速设置。
- **实现**：`ToolBadge` 由静态文本改为可交互条：线名按钮循环直线/波浪/双线/自由画/圈注，直径按钮循环预设笔宽，透明度按钮循环 0.35/0.55/0.8/1.0，颜色圆点单击打开 `ColorPaletteWindow`；`ShowToolBadge()` 同步名称、直径、透明度与颜色点（R89）。

### R90 画线/橡皮光标形态

- **需求**：画线工具钢笔光标，橡皮擦工具橡皮擦光标。
- **实现**：生成 `Resources/pen.cur`（斜置钢笔 + 银色笔尖）与 `eraser.cur`（粉色橡皮块 + 白色擦头），以 `EmbeddedResource` + `LogicalName` 嵌入；`CursorFactory` 从程序集流加载，失败回退 `Cursors.Pen`/`Cursors.Cross`；`SetToolCursor()` 按工具设置光标（画线族用钢笔、擦除族用橡皮、移动用 SizeAll）（R90）。

### R91 右上按钮与空画布提示配色

- **需求**：右上角图钉/关闭面板与整体不协调；空画布提示文字与背景太接近。
- **实现**：`App.xaml` 新增统一主题资源（石墨玻璃 `#353537`、白色描边、冷蓝强调色）；`ActionPanelShape` 改为与灵动岛一致的 `#F2353537`／置顶态 `#F23A3D40`，图钉光晕改冷蓝；关闭按钮 hover 改主题蓝而非整块白遮罩。空画布 `Hint` 改为带石墨玻璃底、白描边、白正文和蓝色副提示的胶囊面板（R91）。

### R92 罗盘配色与同层三态高亮

- **需求**：罗盘 UI 与系统风格统一；同一层级选中后仅降低同级其他项，外层已激活项保持正常。
- **实现**：`CNode` 分类色改为主题蓝/紫/青/暖/玫红，`CreateSectorPath()` 以石墨底色 + 强调色微染渲染，激活项为唯一高亮扇区；`RebuildCompass()` 按 `ringHasSelection` 降级同层非选中项，`isBreadcrumb` 保持外层正常亮度；`_compassPreviewRing` 使预览层用半透明虚线扇区（R92）。

### R93 连线触发外层与预览保护

- **需求**：连线允许触发最外层菜单；被线触发且带下级的项仅半透明预览，下级不可被线触发。
- **实现**：新增 `_compassTrigger[]`（Pointer/Line）与 `_compassPreviewRing`；`UpdateCompass()` 在鼠标离开扇区时用线与最外层中线的交点角度选中该项，标记 Line；带下级则设置 preview ring 并在 `HandleCompassUp()` 中禁止触发分组节点；鼠标真正落入下级扇区时转为 Pointer 正常激活（R93）。

### R94 工具徽标选择面板与选中高亮

- **需求**：左下角悬停遮罩突兀；线型/线径/透明度/颜色要向上弹出选择菜单，点击颜色要能调色；涂鸦选中对象要更醒目。
- **实现**：`MainWindow.xaml` 用 Grid 承载 `ToolBadgeMenu` 与徽标；`BuildToolBadgeMenu()` 以代码生成五组选择按钮，颜色包含“调色板…”并接入 `OpenColorPalette()`；悬停仅改边框为 `#AA6CB6FF`。`AnnotationStroke.CreateVisual()` 增加青色柔光、虚线包围框与端点手柄，填充/文字卡片增加选中标记（R94）。

### R95 涂鸦与图片绝对绑定

- **需求**：缩放后涂鸦不错位，大小与位置跟随图片。
- **实现**：笔迹坐标保持归一化，`Thickness` 改为源图像素单位；`ViewScale` 将宽度换算到屏幕；手绘抖动改为在图像像素空间用固定 seed 生成，绘制中 seed 不变（`_draftSeed`）；文字卡片字号与填充标记按 `ViewScale` 缩放；`.gjpg` 增加 `Style` 字段（R95）。

### R96 标准线与稳定笔触

- **需求**：画线不跳动；新增标准平滑线，单线/双线/波浪/圆圈都跟随线型。
- **实现**：`InkStrokeStyle` + `PathLine/PathWave/PathDouble/PathCircle` 在图像像素空间生成几何；Standard 使用二次中点平滑的 `Path.Stroke`，HandDrawn 使用固定种子的可变宽度 ribbon；罗盘、徽标、元素线型菜单均可切换；`GjpgService` 持久化线型（R96）。

### R97 AI 配置入口与未配置提示

- **需求**：罗盘 AI 增加配置菜单；未配置 API 时提示而非静默失败。
- **实现**：AI 分组新增“配置”→ `App.ShowAiSettings()`；`SettingsWindow(bool aiFocus)` 高亮 AI 分区并聚焦密钥框；`BeginRegionAction()` 先检查 `AiService.IsConfigured`，未配置则提示并跳转设置；框选裁剪修正为减去图片左上角偏移（R97）。

### R98 全场景截图与全局快捷键

- **需求**：三套罗盘、顶部岛、后台快捷键都提供截图；菜单含全屏/窗口/矩形/自由区。
- **实现**：`ScreenshotMode` 四模式；`ScreenshotWindow` 重写，支持全屏直选、枚举顶层窗口点选、矩形拖动、自由封闭区裁剪；`ShotButton` 上下文菜单与 `ScreenshotGroup()` 在三个罗盘树中复用；`HotkeyAction.ScreenshotRegion` + `ScreenshotHotkey` 设置项（默认 Shift+Ctrl+Alt+X）；GDI 捕获异常统一提示不退出（R98）。

### R99 截图崩溃修复

- **需求**：截图导致软件崩溃退出。
- **实现**：`HandleCompassUp()` 先关闭罗盘并释放鼠标捕获，再执行可能打开模态对话框的命令；`StartScreenshot()` 捕获所有异常并使用安全 Owner；截图窗口所有 GDI/位图转换路径包 try/catch（R99）。

### R100 底部幻灯片岛与左右翻页

- **需求**：左右边缘上一张/下一张；底部小白条悬停展开半透明工具栏，中间大两侧小，滚轮滚动，点击全屏展开。
- **实现**：新增 `MainWindow.Filmstrip.cs` 与 `FilmstripHost/Bar/List`；`SetPlaylist()` 支持拖入批次优先，否则读取当前目录；`SyncPlaylistForCurrentImage()` 自动同步；缩略图按需生成并冻结；`PreviewMouseWheel` 横向滚动；左右 `PrevZone/NextZone` 悬停显现并翻页；鼠标按下时排除这些交互区，避免与平移冲突（R100）。

### R101 快捷键设置扩展

- **需求**：后台提供截图快捷键设置并支持全局。
- **实现**：设置窗口增加第四行快捷键输入与冲突提示，`HotkeyHost` 注册 id=4 的 `ScreenshotRegion`，保存时参与必填与重复校验（R101）。

### R102 协同置顶与罗盘/徽标状态同步

- **需求**：工具状态在罗盘与徽标间保持一致。
- **实现**：`SetLineStyle()` 同时更新全局线型与选中笔迹样式；`ShowToolBadge()` 显示当前标准/手绘模式与框选识别/翻译状态；所有罗盘动作在捕获释放后统一调用并刷新徽标（R102）。

### R103 灵动岛描边与半圆弧翻页

- **需求**：灵动岛外边线过亮；左右翻页按钮方形且触发区小。
- **实现**：新增 `ThemeEdgeBrush`（低对比冷灰），灵动岛 `IslandShape` 描边由纯白改为该资源，白色横条触发器保持不变；左右翻页区扩大至 72×228 的透明命中区，内部绘制贴合窗口边缘的半圆弧形 Path（R103）。

### R104 底部幻灯片岛可靠触发与 iOS 邻近放大

- **需求**：悬停底部白条无法展开预览条；预览需要鼠标邻近放大的幻灯片效果。
- **实现**：4px 白条外套 220×28 透明命中区以绕开 WindowChrome 底边拖拽区；`FilmstripHost/BarHit` 共用带 260ms 延时的离开计时器，避免移动途中收起；`FilmstripHost_MouseMove` 以高斯权重为每个 ListBoxItem 设置 Scale/Translate，实现中心放大、两侧缩小的 iOS 式效果（R104）。

### R105 罗盘连线触发防抖

- **需求**：连线触发存在卡顿与断触。
- **实现**：新增 `_compassLineAngle` 与角度差阈值（至少 2.2° 或扇区跨度的 12%），连线命中扇区只在越过阈值后切换；指针环外判定容差扩大到 +11px；连线始终以当前最深可见环为触发目标，减少重建次数（R105）。

### R106 选中效果与端点/尺寸调整

- **需求**：选中对象不要硬外框；线段端点拖动乱飞；圆圈与自由画需支持宽高调整。
- **实现**：移除笔迹虚线包围盒与文字卡片外发光，仅保留柔和底光、手柄与卡片自身强调描边；端点拖动统一在图片空间计算（`_editStartImage` 与 `_editOriginalHandleValue`），修复混合坐标系导致的乱飞；`GetHandles()` 为圆圈/自由画提供对角缩放手柄，`ApplyHandle()` 支持两者缩放（R106）。

### R107 工具徽标按元素分化菜单

- **需求**：徽标悬停遮罩突兀；所有元素共用一套菜单且部分选项无效。
- **实现**：取消悬停变色；`BadgeMenuKind` 分线型/笔触/线径/透明度/颜色五类，点击哪一段只弹对应菜单；选中直线/波浪/双线时名称菜单只列三种线型，圆圈/自由画显示不可转换提示；`ApplyBadgeSize/SetLineStyle/ApplyColorToSelection` 使选项真正作用于选中元素（R107）。

### R108 翻页渲染与轮播开关

- **需求**：翻页卡顿；需要控制末张后的循环行为。
- **实现**：新增 12 项冻结 Bgra32 解码 LRU 缓存，去掉加载时重复布局与重绘；播放列表复用同一 List 实例并只更新选中项；`AppSettings.LoopPlaylist` 与后台复选框控制循环，关闭后边界翻页按钮置灰且点击闪动提示（R108）。

### R109 后台设置分类与滚动布局

- **需求**：设置项全部堆在一页导致超出屏幕；需要分类、滚动条与常规窗口按钮。
- **实现**：`SettingsWindow.xaml` 重写为 860x600 可缩放窗口，左侧 `CategoryList` 六类导航（常规/外观/交互/快捷键/大模型/关于），右侧每类独立 `ScrollViewer`；顶部与底部均提供关闭/取消按钮（R109）。

### R110 大模型配置中心

- **需求**：支持供应商名称、调用地址、接口类型、API、模型库拉取与批量勾选；翻译/识别分别指定默认模型；连接测速与 Codex++ 本地路由一键对接。
- **实现**：新增 `Models/AiProviderModels.cs`（`AiProvider` / `AiModelEntry` / `AiProviderConfig`，翻译与识别默认绑定互相独立）；重写 `Services/AiService.cs` 按用途解析供应商/模型，支持 Chat Completions 与 Responses、`/models` 上游拉取、Stopwatch 连接测速与 8317/1234/11434 等本地路由并发探测；新增 `Views/AiProviderWindow.xaml(.cs)` 提供 Codex++ 风格供应商管理界面；`SettingsService` 将旧版单供应商配置迁移到 `AiConfig`（R110）。

### R111 软件名称全中文

- **需求**：任务管理器、封装产物等仍显示拼音 `GuaishushuViewer`，需全部改为中文且不产生批处理乱码。
- **实现**：主程序与安装器 `.csproj` 的 `AssemblyName/AssemblyTitle/FileDescription` 改为中文；程序集名、exe 名、安装目录、卸载项、快捷方式、文件关联 ProgID/Verb、单实例管道、注册表键全部中文；卸载时同时清理旧拼音键与目录。两个封装 bat 改为纯 ASCII 文本，加入 `chcp 65001`，版本仍从 `<Version>` 读取，exe 名通过 `for` 枚举发布目录获得，不在 bat 中写入任何非 ASCII 字节。GitHub Release 资产上传接口拒绝中文文件名，线上资产改用 `Viewer-v<版本>.exe` 并以中文 label 显示名称，本地产物保持纯中文名（R111）。

### R112 底部 Dock 幻灯片坞

- **需求**：底部预览区方形外观缺少高级感，需要 macOS Dock 式指针邻近悬停放大动画。
- **实现**：`MainWindow.xaml` 把底部面板改为悬浮玻璃胶囊（30px 圆角、渐变玻璃填充、双层白描边、柔光阴影），缩略图改为圆角图标并带高光与选中光环；`MainWindow.Filmstrip.cs` 用高斯权重根据指针 X 计算每张缩略图的缩放、抬升与横向让位，通过 `CompositionTarget.Rendering` 逐帧插值并配合 `BackEase` 弹出/回弹，Dock 本体展开时同步做缩放与淡入（R112）。

### R113 边缘气泡翻页控件

- **需求**：左右半圆翻页区造型生硬，需要在悬停时自然生长为气泡。
- **实现**：`MainWindow.xaml` 用贝塞尔气泡路径、玻璃渐变与流线高光替换半圆 Path，箭头改为圆头描线；`MainWindow.Filmstrip.cs` 通过 ScaleTransform/TranslateTransform 与 `BackEase` 实现从窗口边缘弹性生长、离开收缩，禁用态保留微弱提示，到边界时播放透明度脉冲（R113）。

### R114 取消 Dock 底板并收窄放大干涉

- **需求**：4.3.0 的 Dock 底板太重，用户只想要浮空缩略图与放大效果；邻近图标被放大的数量过多。
- **实现**：移除 `DockGlassBrush/DockEdgeBrush/DockSheenBrush` 与面板描边、高光、计数气泡，`FilmstripPanel` 退化为纯透明坐标容器；缩略图缩到 48px 并加独立柔和投影。高斯 sigma 从 46 降到 26，横向让位系数从 0.24 降到 0.1，最大放大倍率从 1.60 调到 1.50，只保留指针附近极少缩略图的弹性响应（R114）。

### R115 边缘翻页改为流线凸起

- **需求**：上一版气泡仍像独立按钮，用户希望是与窗口边缘融为一体的流线型凸起触发区。
- **实现**：用更修长的贝塞尔凸透镜路径替换气泡，移除完整描边与箭头；渐变从窗口边缘向内侧淡出，只保留一条弧形高光。`ScaleX/ScaleY` 分离，悬停时以 0.28→1 的横向伸展从边缘凸起，纵向只做 0.72→1 的轻微拉伸；禁用与边界脉冲改为极低对比度（R115）。

### R116 截图功能重构

- **需求**：截图仍会导致软件死机，要求彻底检查并重构。
- **实现**：`ScreenshotWindow` 在遮罩打开前用 GDI 一次性缓存原始屏幕，遮罩显示缓存位图，确定时从缓存 `CroppedBitmap` 裁切，彻底避免遮罩自摄与重复 GDI 读屏；`StartScreenshot` 先释放主窗口鼠标捕获/罗盘/草图，临时取消主窗口置顶，遮罩不设 Owner；截图结果通过 `ShowCaptured` 直接显示在现有窗口，`App.OpenBitmap` 也改为复用现有窗口，杜绝多个置顶窗口叠放造成的假死；窗口探测节流到 20 Hz，所有 GDI 与裁剪异常兜底（R116）。

### R117 解除缩略图隐形裁切并修复点击

- **需求**：Dock 放大图标超过容器后被无形遮罩切割，点击缩略图主视图不切换。
- **实现**：扩展 `FilmstripHost` 命中高度到 150px，`ListBox` 与其内部 `ScrollViewer`、`ItemsPanel` 全部关闭 `ClipToBounds`；新增 `FilmstripHost_PreviewMouseLeftButtonUp` 按指针 X 与图标中心的近距离匹配回退导航，放大区域任意位置点击都能打开对应文件（R117）。

### R118 真实 PDF 阅读

- **需求**：上一页/下一页卡顿，因为 PDF 只是缩略图且被混入图片列表。
- **实现**：目标框架升级到 `net10.0-windows10.0.19041.0`，新增 `Services/PdfService.cs` 使用 `Windows.Data.Pdf` 逐页渲染到冻结 Bgra32 位图；`MainWindow` 维护 PDF 文档/页码/页缓存，方向键先翻 PDF 页、到边界再跨文件，并预取相邻页；PDF 缩略图走 Shell 预览服务（R118）。

### R119 按参考图重建边缘流线

- **需求**：翻页触发区按用户提供的红色 S 形曲线构造，但使用全局主题配色。
- **实现**：左右触发区改为 56×200 的贝塞尔 S 曲线（中部内凸约 26px），主题蓝渐变薄翼 + 主题描边 + 蓝色柔光，去掉气泡填充与箭头；悬停时仍从窗口边缘弹性伸展（R119）。

### R120 罗盘最外层连线触发灵敏度

- **需求**：连线快速经过最外层菜单时不能可靠触发。
- **实现**：罗盘打开期间订阅 `CompositionTarget.Rendering`，用 `GetCursorPos` + `PointFromScreen` 按帧轮询真实指针；外环容差 +34px，角度迟滞从 `max(2.2°, span*12%)` 降到 `max(0.7°, span*4%)`；一旦最外层已展开，连线可远距离重新触发该层（R120）。

### R121 边缘翻页流线矮化与配色统一

- **需求**：左右翻页流线凸起过高不协调，边缘线配色突兀，与整体配色不符。
- **实现**：触发区由 72×240 缩为 56×150，凸起内凸从 26px 降到 12px，ScaleY 动画移除只保留横向生长；去掉蓝色发光描边与白色硬线，改为主题玻璃渐变 + 单条柔白高光（R121）。

### R122 截图 disposed object 崩溃与显示方式

- **需求**：截图仍偶发 "Cannot access a disposed object: ByteStreamGeometryContext" 崩溃；结果应在新窗口钉桌面而非刷新当前窗口。
- **实现**：`ScreenshotWindow` 彻底移除 StreamGeometry，遮罩改用 4 个 Rectangle、框选用 Border、自由区用 Polyline、裁剪用不可变 PathGeometry，杜绝上下文释放后被渲染；`App.OpenBitmap` 恢复为新建置顶窗口，`StartScreenshot` 改回调用它（R122）。

### R123 罗盘连线触发再次增强

- **需求**：虚线经过最外层菜单快速划过时仍断触。
- **实现**：罗盘打开期间 WPF 鼠标移动事件不再写入选择（其坐标在快速拖动时会滞后并覆盖准确轮询值），仅由 `CompositionTarget.Rendering` 帧轮询 GetCursorPos 驱动；移除角度迟滞门控，每帧都按当前连线角度重算扇区；各环外扩容差随罗盘缩放放大，最外环容差 +70px（R123）。

### R124 Dock 放大算法重建

- **需求**：底部仿 macOS 悬停放大逻辑观感差。
- **实现**：按 Apple 专利的余弦衰减重建，`scale = rest + (peak-rest)*cos(pi*d/2W)`，在影响半径边缘斜率归零、无硬切断；邻图标位移由两侧缩放差推导，行内精确平铺无重叠无空隙；加入阻尼弹簧积分产生回弹；改用展开时缓存的布局中心替代实时反推变换，消除抖动（R124）。

### R125 罗盘统一配色、字体与大小设置

- **需求**：罗盘多色配色不协调，字太小且模糊，需要后台调节罗盘大小。
- **实现**：`CNode` 忽略旧彩虹配色，所有扇区统一使用主题蓝（分组）与柔蓝（叶子），仅调色板叶子用小色点保留功能；字号随罗盘缩放提升（glyph 16-18、标签 12），启用 Display 文本格式 + ClearType + 像素吸附 + 轻投影去掉模糊；连线改为主题蓝流动；后台交互页新增罗盘大小滑杆（70%-160%），写入 `AppSettings.CompassSize`，环半径与字号整体缩放（R125）。

### R126 Dock 指针输入补全与调试清理

- **需求**：底部悬停触发条位于展开后的 Dock 容器之上，横向划过时原事件没有继续更新指针坐标，放大目标可能停在上一次位置；同时排查用的磁盘日志不能进入交付版本。
- **实现**：`FilmstripBarHit` 与 `FilmstripHost` 的 MouseMove 统一走 `UpdateFilmPointer`，把指针位置换算到 `FilmstripPanel` 坐标系；保留逐帧懒计算稳定中心与余弦衰减/弹簧平铺算法；删除 `C:/wb_dock_debug.log` 诊断写入。重新编译主程序与安装器均 0 警告、0 错误，单文件与安装包均已生成（R126）。

### R127 安装界面许可协议中文化

- **需求**：安装向导的许可协议正文仍为英文，与全中文软件定位不符。
- **实现**：新增 `LICENSE.zh-CN.txt` 中文 MIT 许可文本；安装器改为内嵌该中文文本（资源逻辑名仍为 `LICENSE.txt`），许可显示框字体由 Consolas 改为 Microsoft YaHei UI，保证中文显示清晰；仓库根目录保留英文原文用于通用许可声明。重新封装 v4.5.1 单文件与安装包，并在安装包二进制中确认中文声明存在、英文正文已不存在（R127）。

### R128 完整安装与独立卸载工具

- **需求**：原安装程序只复制主程序与快捷方式，卸载仅靠主程序的 `--uninstall` 粗放删除固定目录，没有独立卸载程序，也无法完整清除文件关联、右键菜单与用户配置。
- **实现**：新增 `src/WBUninstaller/` 独立 WinForms 卸载器，安装器内嵌并在目标目录释放 `怪叔叔看图卸载.exe`；开始菜单程序组提供“卸载 怪叔叔看图”入口，卸载注册项写入独立 `UninstallString` 与 `QuietUninstallString`。卸载器可关闭正在运行的主程序，清理桌面/开始菜单快捷方式、ProgID、OpenWithProgids、SystemFileAssociations 右键动词、UserChoice 默认记录、关联提示、用户配置目录及卸载注册项，并在退出后自删。主程序的 `--uninstall` 改为转发到独立卸载器（R128）。

### R129 左右翻页流线再扁平化

- **需求**：上一版 S 形边缘凸起反而更突兀，与用户截图中的细长红线意象不符。
- **实现**：触发区保留 56×150 透明命中区，可见曲线改为 28×132 的细长月牙，最大内凸从 12px 降到 5px；曲线控制点拉长以减少折角，悬停宽度由 100% 降到 85%，填充与高光 alpha 同步降低，形成贴边流动感而非厚重凸起（R129）。

### R130 Dock 单图标聚焦放大与点击修复

- **需求**：底部 macOS Dock 放大混乱、邻图标一起变形，点击缩略图不能稳定打开对应图片。
- **实现**：放大逻辑改为“当前指针下只有一个图标聚焦”：用实时渲染包围盒定位悬停项，仅该项放大到 1.65 倍并上浮，其余图标保持 0.85 倍并各向两侧让出一半增量宽度；弹簧积分保留回弹但不再让邻图标膨胀。为 `FilmstripHost` 与 `FilmstripPanel` 增加透明背景使其覆盖放大溢出区可命中，点击处理复用同一包围盒索引，直接打开对应图片（R130）。

---

## 11. 版本变更记录

### 4.5.2（2026-11-20）

- 安装器释放独立卸载程序，开始菜单提供卸载入口，卸载器完整清理程序文件、快捷方式、关联、配置与注册信息（R128）。
- 左右翻页流线进一步扁平化，降低凸起与高光强度（R129）。
- 底部 Dock 改为仅放大指针下的单张缩略图，并修复点击打开对应图片（R130）。

### 4.5.1（2026-11-20）

- 安装向导许可协议改为中文 MIT 文本，许可框改用微软雅黑显示（R127）。

### 4.5.0（2026-11-19，2026-11-20 收尾）

- 边缘翻页流线矮化并统一为主题配色（R121）。
- 截图系统移除 StreamGeometry 彻底消除 disposed object 崩溃，结果改为在新窗口钉桌面显示（R122）。
- 罗盘连线触发改为单源帧轮询 + 无迟滞重算，消除快速划过断触（R123）。
- 底部 Dock 放大按 Apple 余弦算法重建，精确平铺 + 弹簧回弹（R124）。
- 罗盘菜单统一主题配色，字体放大锐化，后台新增罗盘大小调节（R125）。
- 补全悬停条与 Dock 容器的指针坐标同步，清理临时日志并重新封装交付物（R126）。

### 4.4.0（2026-11-19）

- 截图系统在遮罩打开前缓存原始屏幕并从中裁切，不再自摄或重复读屏；结果直接替换当前窗口内容，彻底解决截图后假死与多窗口叠放（R116）。
- 底部放大缩略图取消隐形裁切，点击放大区域可正确打开对应文件（R117）。
- 新增真实 PDF 逐页阅读、页面缓存与相邻页预取；上一页/下一页先翻 PDF 页（R118）。
- 左右翻页触发区按参考图重建为 S 形流线曲线，使用主题配色（R119）。
- 罗盘最外层菜单连线触发改为逐帧轮询 + 低迟滞，快速划过也能触发（R120）。

### 4.3.1（2026-11-19）

- 底部 Dock 底板、描边、高光与计数气泡全部取消，仅保留带独立投影的浮空圆角缩略图（R114）。
- 放大影响半径显著收窄，只有指针附近少量缩略图响应，横向干涉同步减小（R114）。
- 左右翻页区改为与窗口边缘融合的流线型凸起触发区，去掉按钮化描边与箭头（R115）。
- 编译 0 警告 0 错误。

### 4.3.0（2026-11-19）

- 底部预览区重构为 macOS Dock 风格玻璃幻灯片坞，缩略图高斯邻近放大、抬升、横向让位与逐帧弹性插值（R112）。
- 左右翻页区重构为有机气泡，悬停从边缘弹性生长，禁用与边界提示保持可用（R113）。
- 编译 0 警告 0 错误。

### 4.2.1（2026-11-19）

- 后台设置重构为六类导航 + 独立滚动区 + 可缩放窗口（R109）。
- 新增大模型配置中心：多供应商分组、上游模型库批量勾选、Chat Completions/Responses、翻译与识别独立默认模型、连接测速、Codex++ 本地路由一键对接（R110）。
- 软件名称全链路中文，任务管理器与所有产物显示“怪叔叔看图”；封装 bat 纯 ASCII 化，彻底规避中文乱码（R111）。
- 编译 0 警告 0 错误。

### 4.1.0（2026-11-19）

- 灵动岛外描边改为窗口同款低对比风格，翻页区改半圆弧并扩大触发（R103）。
- 底部幻灯片岛可靠展开并实现 iOS 邻近放大动画（R104）。
- 罗盘连线触发加入角度迟滞，消除卡顿与断触（R105）。
- 选中对象仅适度增强；端点拖动修复；圆圈与自由画支持宽高手柄（R106）。
- 左下角徽标四段独立菜单，按元素/工具匹配且选项真实生效（R107）。
- 图片解码缓存 + 翻页减负；后台轮播开关与边界闪动提示（R108）。
- 编译 0 警告 0 错误。

### 4.0.0（2026-11-19）

- 主题配色统一：右上按钮、空画布提示、罗盘三套菜单全部改为石墨玻璃 + 白描边 + 冷强调色（R91/R92）。
- 罗盘同层三态与连线触发外层/下层预览保护（R92/R93）。
- 工具徽标上弹选择菜单、内置调色板入口、选中对象高亮（R94）。
- 涂鸦与图片绝对绑定，标准/手绘双线型且绘制稳定（R95/R96）。
- AI 配置入口、未配置提示与框选区域修复（R97）。
- 全场景四模式截图、全局截图快捷键与截图崩溃修复（R98/R99/R101）。
- 底部幻灯片岛、目录/拖入批次轮播与左右翻页区（R100）。
- 编译 0 警告 0 错误；主程序与安装器均通过 Release 构建。

### 3.0.0（2026-11-19）

- 罗盘重构：连线精修且纯视觉化（R80）、三态高亮与外层粘滞（R81）、画布/图片/元素三套上下文菜单（R82）。
- 元素编辑：端点拖拽、圆圈调长宽、移动、Delete 删除、线型受限转换（R83）；油漆桶填充（R84）。
- 透明度与荧光笔高亮，罗盘面板与左下角快捷设置（R85）。
- 仿微信截图钉桌面新窗口置顶（R86）；大模型 API 翻译/手写识别与框选处理（R87）。
- .gjpg 可编辑工程格式，保存/另存为与打开自动还原全部痕迹（R88）。
- 左下角工具徽标可点击快设（R89）；钢笔与橡皮擦自定义光标（R90）。
- 编译 0 警告 0 错误，单文件与安装程序封装产物版本均为 3.0.0。

### 2.0.0（2026-11-18）

- 全场景更换品牌 Logo 为龙形 G（墨绿龙首、朱红祥云、米色回纹、透明背景），覆盖多尺寸 ICO 与 PNG，主窗口/托盘/任务栏/资源管理器/关于/安装程序统一生效（R79）。
- 版本号升至 2.0.0，README 与开发档案同步。

### 1.9.0（2026-11-18）

- 罗盘“工具”分类新增“移动”功能：可拖动任意已绘制笔迹，位移按图片归一化坐标计算，缩放/平移后仍精确（R76）。
- 笔迹渲染改造为手绘风格：可变宽度 ribbon 笔触 + 确定性抖动；直线、双线、波浪线、圈注均改为模拟手绘的两道重叠/不规则形态（R77）。
- 恢复 📌 emoji 图钉外形，加入顶入/拔出回弹动画（R78）。
- 版本号升至 1.9.0，README 与开发档案同步。

### 1.8.0（2026-11-18）

- 修复安装向导顶部 Logo 区覆盖右侧内容：根布局改三行 `TableLayoutPanel`（R70）。
- 安装时个别格式默认关联失败不再中止安装，改为警告并提示；`SetUserChoice` 增加就地写入兜底（R71）。
- 灵动岛彻底删除时间显示（R72）。
- 主窗口 / 关于 / 设置三窗口协同置顶，激活者升到最上层（R73）。
- 新增全局快捷键冲突检测：占用探测 + `.lnk` 占用者扫描 + 系统保留组合表 + 实时提示（R74）。
- 标注罗盘重构为分层分组菜单：分类逐级展开/收回、悬停文字提示、三层颜色选择、黄青流动置顶指示线（R75）。
- 版本号升至 1.8.0，README 与开发档案同步。

### 1.7.0（2026-11-18）

- 修复 1.6.0 单文件无界面/无托盘：图钉悬停条件由 `MultiTrigger` 改为 `MultiDataTrigger`（R66）。
- 安装主体改用 `TableLayoutPanel` 两列布局，彻底消除左侧步骤区覆盖右侧内容区（R67）。
- 新增 `DefaultAssociationService`：静默写入 UserChoice 哈希自动成为图片默认程序，并注入“用怪叔叔看图打开”右键菜单；安装与设置均不再弹出默认应用窗口（R68）。
- 新增图片标注与 SolidWorks 风格罗盘菜单：直线/波浪/双线/自由画/圈注/橡皮擦全套功能，12 扇区、跟随指示线、可配置触发键（R69）。
- 版本号升至 1.7.0，README 与开发档案同步。

### 1.6.0（2026-11-18）

- 修复安装向导许可页“下一步”永久灰显：根因是接受复选框被裁切到内容区外；重建向导并加入底部实时状态栏、目标位置定时校验、分阶段安装日志与错误详情/重试（R63）。
- 安装界面改为左侧五步导航 + 右侧内容 + 底部状态栏，统一字体、间距与高亮样式（R64）。
- 重做主题 Logo（照片框 + 眨眼大叔 + 琥珀图钉），生成 7 尺寸 ico 与 PNG，覆盖主窗口、设置/关于、托盘、任务栏、资源管理器、任务管理器、快捷方式、文件关联与安装程序（R65）。
- 版本号升至 1.6.0，README 与开发档案同步。

### 1.5.0（2026-11-18）

- 单文件封装只输出 `artifacts/single/GuaishushuViewer-v1.5.0.exe`；安装包封装只输出 `artifacts/setup/GuaishushuViewer-v1.5.0-Setup.exe`，发布暂存目录用后即删（R59）。
- 置顶与关闭合并到同一块岛形面板：单底板、双按钮、中间分隔线，顶部仍与窗口齐平（R60）。
- 图钉改为矢量钉帽+钉针，置顶/取消置顶时以 BackEase 动画呈现直立发光的钉入状态与倾斜抬起的拔出状态（R61）。
- 安装程序改为常规 Windows 五步向导：欢迎、许可协议、目标位置、安装进度、完成；内嵌 MIT 许可全文，支持自定义安装目录与完成后运行/打开默认应用确认（R62）。
- 版本号升至 1.5.0，README 与开发档案同步。

### 1.4.0（2026-11-18）

- 双向适配按钮换成双层形象图标：上层为目标（窗口/图片），下层为来源，功能指向更直观（R55）。
- 置顶与关闭按钮顶部与窗口齐平，外框改用灵动岛构建法（相切顶边 + 镜像凹肩 + 圆角底部）（R56）。
- 新增 `src/WBInstaller/` 常规 exe 安装程序：内嵌主程序、安装到用户目录、注册关联/快捷方式/卸载项，最后打开系统默认应用确认窗口；`封装安装包.bat` 同时产出 exe 与 ZIP（R57）。
- 格式清单集中到 `SupportedFormats.cs`，WIC 直接解码扩展至 HEIC/AVIF/JXL/JP2/TGA 等；新增 Shell 缩略图回退，可预览 SVG、PDF、XMind、draw.io、Visio 等流程图/脑图文件（R58）。
- 关于窗口与 README 明确列出全部支持格式与预览限制；版本号升至 1.4.0。

### 1.3.0（2026-11-18）

- 清理全部生成产物（根目录 exe、`dist/`、`bin/`、`obj/`），仓库回归纯源码；`.gitignore` 屏蔽 `artifacts/`、`*.exe` 等生成物（R50）。
- 建立 `artifacts/single`、`artifacts/setup`、`artifacts/setup-stage` 封装专用目录；两个 bat 固定代码、自动从 csproj `<Version>` 读取版本号，产物不再写回根目录（R51）。
- `<Version>` 成为唯一版本来源：`AppInfo.Version` 从程序集信息版本派生，安装包脚本从 exe 的 ProductVersion 读取并去掉 Git 哈希后缀；当前版本升为 1.3.0（R51）。
- 置顶图钉按状态切换亮底直立（钉入）与暗底倾斜（拔起），快捷键、按钮、托盘菜单共用同一状态源（R52）。
- 灵动岛缩小为 200x34，右顶角按左顶角严格镜像，消除向下凸起的反向圆角（R53）。
- “窗口适配图片”改为以图片当前屏幕位置为锚点定位窗口，边界钳制残差由图片偏移吸收（R54）。
- 重新封装验证：单文件 `GuaishushuViewer-v1.3.0.exe`、安装包 `GuaishushuViewer-v1.3.0-Setup.zip` 均由固定 bat 自动生成。

### 1.2.0（2026-11-18）

- 修正滚轮缩放锚点，改为以鼠标位置为中心缩放。
- 窗口移动使用抓取手势；后续按用户最新要求改为 Windows 系统 `Cursors.Hand`，删除自绘 `grab.cur`。
- 新增顶部刘海控制条：置顶、双向适配、图片调整、设置、关于；后按截图重构为白色细线 + 悬停平滑展开的岛形。
- 新增右上角悬停展开关闭按钮。
- 使用 DWM 抗锯齿圆角替代 GDI 窗口区域，消除边缘锯齿。
- 新增背景颜色、面板透明度设置。
- 新增图像增强管线：亮度、对比度、饱和度、边缘保护去噪、锐化、智能优化与一键还原。
- 新增关于窗口与打赏二维码；托盘右键菜单底部显示软件名与版本。
- 更名软件与仓库，新增单文件与安装包封装脚本。
- 安装包自动注册文件关联，并打开系统默认程序确认窗口。
- GitHub 仓库、Issue 模板、标签、里程碑、Release 资产完成。
- 本档案升级为唯一权威、逐需求附实现逻辑与代码片段的严谨开发档案。
- 修正透明度语义：`AllowsTransparency=True` + 窗口背景全透明，透明度只写入面板背景 Brush；图片与控件保持不透明，未覆盖区域按设定透明度显示桌面。
- 更正软件名称为“怪叔叔看图”，并同步全项目文案、设置与注册表显示值。
- 托盘右键菜单版本状态改为两行显示，避免被菜单宽度裁切。
- 置顶按钮改为矢量图钉，并移入右上角与关闭按钮共用的悬停伸缩区。
- 新增 `ImageViewport` 画布裁切，图片缩放/平移始终限定在窗口当前客户区，修复无形窗口切割与图片盖住控件的问题。
- 按截图重新分析并重建灵动岛轮廓：顶部向两侧外扩、肩部内圆角、下部收拢、底部大圆角。
- 按用户 ASCII 模型再次重构灵动岛为 240x40 双翼形态：顶部双翼外伸、内圆角肩部、直边、底部收拢圆角（R46，替代 R45）。
- 修复“窗口适配图片”后画布留白：按隐形缩放边框差值计算外框，并在布局回调中按最终客户区锁定缩放，四边与图片重合（R47）。
- 置顶图钉由自绘矢量改为系统 📌 Emoji 样式（R48）。
- 灵动岛顶边改为与窗口顶边相切、去掉外伸尾部，顶角与底角统一为 16 半径圆角，呈“从窗口边缘长出”的观感；R46 双翼形态废弃，由 R49 替代。
- 本次修改后已重新生成带版本号与固定名称的单文件与安装包；Remote Release 资产尚未同步，重新分发前需上传。

### 1.0.x

- WPF 无边框置顶面板、全局快捷键、命名管道单实例、基础图片显示与拖拽、默认关联候选。

---

## 12. 后续 AI 必读约束

1. 不要凭常识重写架构；本项目的稳定点是 `WindowChrome` + 显式 `_zoom` + 短时动画 + 静态 DWM 合成。
2. 不要恢复被明确废弃的方案：自定义非客户区白边修复、GDI 圆角 region、`TransformGroup` 中心缩放、自绘 `.cur` 光标、IExpress 安装包、静默改写 UserChoice、R45 的 232x34 岛形路径、R46 的双翼外伸岛形路径与 `IslandHairline` 全宽细线、自绘矢量图钉、把图片显示尺寸直接当窗口外框尺寸的旧 `FitWindowToImage`。
3. 每次改动后同步更新本档案的：需求索引、对应条目、代码片段、版本变更记录。
4. 改版本时必须同时改 `AppInfo.Version`、`.csproj`、README、关于窗口、托盘、Release 与本档案。
5. 若用户要求“保持和软件当前状态一致”，先读取源码再改本档案，禁止只改文档不改代码或只改代码不改文档。
