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
| 程序集名 | `GuaishushuViewer` |
| 当前版本 | `1.2.0` |
| 仓库 | `https://github.com/yysdgss/guaishushu-image-viewer` |
| Release | `https://github.com/yysdgss/guaishushu-image-viewer/releases/tag/v1.2.0` |
| 许可 | MIT |
| 目标框架 | `net10.0-windows` |
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
    <Version>1.2.0</Version>
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
| `packaging/install.cmd` | 安装包内安装脚本 |
| `封装单文件运行包.bat` | 生成单文件 exe |
| `封装安装包.bat` | 生成 ZIP 安装包 |
| `.github/ISSUE_TEMPLATE/` | GitHub Issue 模板 |
| `DEVELOPMENT.md` | 本开发档案 |
| `README.md` | 用户说明 |
| `LICENSE` | MIT 许可 |
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

## 11. 版本变更记录

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
