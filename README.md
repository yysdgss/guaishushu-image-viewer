# 怪叔叔看图

一款面向 Windows 11 的极简无边框参考图查看器：图片置顶、滚轮以鼠标为中心缩放、抓取手势移动窗口，并内置亮度/对比度/饱和度/去噪/锐化调整。

- 当前版本：**1.6.0**
- 技术栈：**WPF / .NET 10（Windows）**
- 许可：**MIT 免费开源**

## 核心功能

- 无边框面板，顶部灵动岛控制条（顶边相切、左右凹肩镜像、底部圆角）；适配按钮为双层图标：目标层在上（图片→窗口用窗口图标，窗口→图片用图片图标）；右上角悬停展开同一个岛形面板，面板上承载置顶与关闭两个按钮。置顶时图钉琥珀点亮、直立钉入并发光；取消时图钉变灰、倾斜并抬起拔出
- 图片画布始终裁切到窗口当前客户区，缩放/平移不会越界或遮挡控件
- 图片始终置顶；快捷键 `Shift+Ctrl+Alt+T` 切换
- 滚轮以鼠标位置为中心缩放，窗口尺寸不变
- `Ctrl + 滚轮` 图片与窗口同步缩放（修饰键可改）
- 左键拖动平移图片；右键 / 中键 / 空格+左键使用 Windows 系统手型抓取光标移动窗口
- `Shift+Ctrl+Alt+R` 窗口适配图片，窗口四边与图片当前显示尺寸完全重合、无留白；`Shift+Ctrl+Alt+S` 图片等比适配窗口
- 单窗口 / 多窗口模式；拖拽图片文件到窗口；注册为图片默认查看方式
- 背景颜色、面板背景透明度（图片不受影响）、圆角、缩放步长等后台设置
- 智能优化预设与一键还原；亮度、对比度、饱和度、边缘保护去噪、锐化
- 统一主题 Logo（照片框＋眨眼大叔＋琥珀图钉）已应用到主窗口、设置/关于窗口、托盘、任务栏、资源管理器、任务管理器、快捷方式与安装程序；关于窗口展示同款图标

##  Logo 与品牌

新 Logo 用深色圆角底、米白照片框、戴眼镜眨眼的大叔与琥珀色图钉表达“怪叔叔看图”与置顶看图主题。`Assets/app.ico` 提供 16/24/32/48/64/128/256 多尺寸，`Assets/app.png` 用于界面内展示。

## 操作

| 操作 | 效果 |
| --- | --- |
| 鼠标滚轮 | 以鼠标位置为中心缩放图片 |
| Ctrl + 滚轮 | 图片与窗口同步缩放 |
| 左键拖动 | 平移图片 |
| 右键拖动 | 抓取并移动整个面板 |
| Shift+Ctrl+Alt+T | 切换置顶 |
| Shift+Ctrl+Alt+R | 窗口适配当前图片 |
| Shift+Ctrl+Alt+S | 图片适配当前窗口 |
| 顶部灵动岛 | 适配 / 图片调整 / 设置 / 关于 |
| 右上角悬停 | 展开图钉置顶与关闭按钮 |

## 支持的格式

直接解码：JPG/JPEG/JFIF/JPE、PNG、BMP/DIB、GIF、WebP、TIFF、ICO、HEIC/HEIF、AVIF、JXL、JP2/J2K、WDP/HDP、EXIF、TGA、PCX、PBM/PGM/PPM/PNM 等。

Windows 缩略图预览：SVG/SVGZ、EMF/WMF、PDF、PSD/AI/EPS，以及 XMind(`.xmind`)、FreeMind(`.mm`)、draw.io(`.drawio`)、Visio(`.vsdx/.vsd/.vdw`) 等流程图与脑图文件。此类文件依赖系统已安装对应软件来生成缩略图，查看的是系统提供的预览位图。

## 下载

- [单文件运行包（Releases）](../../releases)：`GuaishushuViewer-v1.6.0.exe`
- [Windows 安装程序（推荐）](../../releases)：`GuaishushuViewer-v1.6.0-Setup.exe`
- 严谨开发档案：[DEVELOPMENT.md](DEVELOPMENT.md)（每项需求均附实现逻辑、方式与当前代码片段）

> 运行 `GuaishushuViewer-v1.6.0-Setup.exe` 会进入常规 Windows 安装向导：左侧步骤导航，右侧内容，底部实时状态检查。流程为欢迎 → 许可协议（必须勾选接受）→ 选择目标位置（自动检查可写性与空间）→ 安装进度（分阶段日志与错误详情，失败可重试）→ 完成。它把程序安装到所选目录（默认 `%LOCALAPPDATA%\GuaishushuViewer`），创建桌面/开始菜单快捷方式、注册文件关联与卸载项；完成时可选择运行程序并打开系统“默认应用”确认窗口。由于 Windows 11 保护用户的默认应用选择，请在弹出窗口中选择“怪叔叔看图”并勾选始终使用。

## 从源码构建

需安装 .NET 10 Desktop SDK：

```bat
封装单文件运行包.bat
封装安装包.bat
```

输出统一位于 `artifacts/`：`artifacts/single/` 只有 `GuaishushuViewer-v<版本号>.exe`，`artifacts/setup/` 只有 `GuaishushuViewer-v<版本号>-Setup.exe`。两个批处理是固定代码，版本号只从 `src/WBImageViewer/WBImageViewer.csproj` 的 `<Version>` 读取；发布暂存目录会在脚本结束时自动删除，仓库根目录不再残留生成产物。安装程序源码位于 `src/WBInstaller/`，它链接查看器的格式清单，内嵌 MIT 许可协议与发布后的主程序。

## 打赏作者

如果这款软件对你有帮助，可以请作者喝杯奶茶：

<img src="src/WBImageViewer/Assets/donate.png" width="220" alt="打赏二维码" />

“你可以请作者喝杯奶茶提提神，鼓励作者贡献更多适用作品”
