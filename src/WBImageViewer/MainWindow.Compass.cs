using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WBImageViewer.Models;
using WBImageViewer.Services;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace WBImageViewer;

/// <summary>
/// Hierarchical radial compass. Three context-sensitive trees (canvas / image / element),
/// a purely decorative guide line (never used for hit-testing), three contrast states and
/// sticky outer rings so a submenu survives small pointer drift.
/// </summary>
public partial class MainWindow
{
    private enum CompassAction
    {
        None, Pointer, Move, Line, Wave, DoubleLine, Freehand, Circle, Fill,
        EraserFree, EraserClick, EraserBigger, EraserSmaller,
        Size1, Size2, Size3, Size4, Size5,
        SetColor, OpenPalette, ColorCycle, ClearAll,
        OpacityHighlight, OpacityHalf, OpacitySolid, HighlighterToggle,
        RegionTranslate, RegionOcr,
        // canvas context
        OpenImage, SaveImage, SaveAs, Screenshot, ToggleTopMost, Settings, About, ExitTool,
        // element context
        DeleteElement, DuplicateElement, ElemLine, ElemWave, ElemDouble,
        ElemThicker, ElemThinner
    }

    private sealed class CompassNode
    {
        public string Label { get; }
        public string Glyph { get; }
        public string Description { get; }
        public Color Accent { get; }
        public CompassAction Action { get; }
        public Color? TargetColor { get; }
        public List<CompassNode> Children { get; }
        public bool IsGroup => Children.Count > 0;

        public CompassNode(string label, string glyph, string description, Color accent,
            CompassAction action = CompassAction.None, Color? targetColor = null,
            IEnumerable<CompassNode>? children = null)
        {
            Label = label;
            Glyph = glyph;
            Description = description;
            Accent = accent;
            Action = action;
            TargetColor = targetColor;
            Children = children?.ToList() ?? new List<CompassNode>();
        }
    }

    private static readonly double[] CompassRingInner = { 34, 74, 114, 154 };
    private static readonly double[] CompassRingOuter = { 70, 110, 150, 190 };

    private static CompassNode CNode(string label, string glyph, string desc, byte r, byte g, byte b,
        CompassAction action = CompassAction.None, Color? target = null, IEnumerable<CompassNode>? children = null) =>
        new(label, glyph, desc, Color.FromArgb(236, r, g, b), action, target, children);

    private static List<CompassNode> ColorLeaves() => new()
    {
        CNode("红", "●", "红色笔迹", 240, 72, 72, CompassAction.SetColor, Color.FromRgb(240, 72, 72)),
        CNode("橙", "●", "橙色笔迹", 244, 140, 52, CompassAction.SetColor, Color.FromRgb(244, 140, 52)),
        CNode("黄", "●", "黄色笔迹", 236, 200, 56, CompassAction.SetColor, Color.FromRgb(236, 200, 56)),
        CNode("绿", "●", "绿色笔迹", 80, 206, 108, CompassAction.SetColor, Color.FromRgb(80, 206, 108)),
        CNode("青", "●", "青色笔迹", 56, 208, 214, CompassAction.SetColor, Color.FromRgb(56, 208, 214)),
        CNode("蓝", "●", "蓝色笔迹", 64, 150, 246, CompassAction.SetColor, Color.FromRgb(64, 150, 246)),
        CNode("紫", "●", "紫色笔迹", 168, 110, 226, CompassAction.SetColor, Color.FromRgb(168, 110, 226)),
        CNode("粉", "●", "粉色笔迹", 238, 118, 176, CompassAction.SetColor, Color.FromRgb(238, 118, 176)),
        CNode("黑", "●", "黑色笔迹", 32, 36, 44, CompassAction.SetColor, Color.FromRgb(32, 36, 44)),
        CNode("白", "●", "白色笔迹", 244, 246, 250, CompassAction.SetColor, Color.FromRgb(244, 246, 250)),
        CNode("调色板", "◈", "打开自定义调色板", 200, 200, 210, CompassAction.OpenPalette)
    };

    private static List<CompassNode> OpacityLeaves() => new()
    {
        CNode("高亮", "◑", "PPT 演讲式高亮（半透明）", 244, 206, 72, CompassAction.OpacityHighlight),
        CNode("半透明", "◐", "约 55% 不透明度", 150, 180, 210, CompassAction.OpacityHalf),
        CNode("实心", "●", "完全不透明", 220, 224, 232, CompassAction.OpacitySolid),
        CNode("荧光笔", "▤", "切换荧光笔模式", 176, 220, 96, CompassAction.HighlighterToggle)
    };

    /// <summary>Image context: the full ink toolset.</summary>
    private static List<CompassNode> BuildCompassTree() => new()
    {
        CNode("形状", "✎", "线条与圈注工具", 92, 150, 210, children: new[]
        {
            CNode("直线", "─", "绘制直线", 208, 92, 72, CompassAction.Line),
            CNode("波浪线", "∿", "绘制波浪线", 72, 152, 196, CompassAction.Wave),
            CNode("双线", "═", "绘制双线", 132, 92, 196, CompassAction.DoubleLine),
            CNode("自由画", "✎", "自由手写", 92, 168, 116, CompassAction.Freehand),
            CNode("圈注", "◯", "手绘圈注", 196, 152, 72, CompassAction.Circle),
            CNode("填充", "◧", "油漆桶：填充封闭区域", 224, 168, 72, CompassAction.Fill)
        }),
        CNode("颜色", "◐", "笔迹颜色 / 调色板", 208, 120, 190, children: ColorLeaves()),
        CNode("透明", "◑", "不透明度与荧光笔", 150, 180, 210, children: OpacityLeaves()),
        CNode("粗细", "↕", "笔迹粗细", 96, 140, 168, children: new[]
        {
            CNode("极细", "·", "1 px", 128, 168, 200, CompassAction.Size1),
            CNode("细", "•", "2 px", 128, 168, 200, CompassAction.Size2),
            CNode("中", "●", "4 px", 128, 168, 200, CompassAction.Size3),
            CNode("粗", "⬤", "7 px", 128, 168, 200, CompassAction.Size4),
            CNode("极粗", "◉", "11 px", 128, 168, 200, CompassAction.Size5)
        }),
        CNode("橡皮", "▱", "擦除工具与擦头大小", 120, 128, 140, children: new[]
        {
            CNode("自由擦", "▱", "按住左键自由擦除", 156, 168, 184, CompassAction.EraserFree),
            CNode("点擦", "⌫", "点击擦除整个笔迹", 168, 128, 128, CompassAction.EraserClick),
            CNode("擦头＋", "＋", "增大擦头", 96, 168, 196, CompassAction.EraserBigger),
            CNode("擦头－", "－", "减小擦头", 96, 168, 196, CompassAction.EraserSmaller)
        }),
        CNode("AI", "✧", "框选翻译与文字识别", 176, 110, 210, children: new[]
        {
            CNode("翻译", "文", "框选区域并翻译", 92, 176, 210, CompassAction.RegionTranslate),
            CNode("识别", "◉", "框选区域识别文字（含手写）", 120, 200, 150, CompassAction.RegionOcr)
        }),
        CNode("工具", "⚙", "指针、移动、清空与退出", 168, 156, 96, children: new[]
        {
            CNode("指针", "✥", "返回指针（停止标注）", 82, 116, 168, CompassAction.Pointer),
            CNode("移动", "✜", "拖动已绘制的标注元素", 92, 176, 210, CompassAction.Move),
            CNode("换色", "◐", "轮换预设颜色", 92, 176, 196, CompassAction.ColorCycle),
            CNode("清空", "✕", "清空全部标注", 208, 88, 88, CompassAction.ClearAll),
            CNode("退出", "⏻", "退出当前工具，返回正常界面", 150, 150, 160, CompassAction.ExitTool)
        })
    };

    /// <summary>Canvas context (blank canvas / no image): ordinary software commands.</summary>
    private static List<CompassNode> BuildCanvasCompassTree() => new()
    {
        CNode("文件", "▤", "打开与保存", 92, 150, 210, children: new[]
        {
            CNode("打开", "↥", "打开图片或 .gjpg", 120, 180, 220, CompassAction.OpenImage),
            CNode("保存", "▣", "保存到当前文件（含标注）", 120, 200, 160, CompassAction.SaveImage),
            CNode("另存为", "⇨", "另存为 .gjpg 可编辑工程", 200, 180, 120, CompassAction.SaveAs)
        }),
        CNode("截图", "✂", "屏幕截图并钉在桌面", 224, 120, 96, children: new[]
        {
            CNode("区域截图", "▭", "框选屏幕区域并固定显示", 232, 130, 96, CompassAction.Screenshot)
        }),
        CNode("窗口", "▢", "窗口控制", 150, 140, 200, children: new[]
        {
            CNode("置顶", "📌", "切换置顶", 220, 180, 90, CompassAction.ToggleTopMost),
            CNode("设置", "⚙", "后台设置", 150, 160, 190, CompassAction.Settings),
            CNode("关于", "i", "关于本软件", 150, 190, 210, CompassAction.About)
        }),
        CNode("退出", "✕", "关闭罗盘 / 返回正常界面", 200, 88, 88, children: new[] { CNode("返回", "↩", "退出工具", 150, 150, 160, CompassAction.ExitTool) })
    };

    /// <summary>Element context: edit the element under the pointer.</summary>
    private static List<CompassNode> BuildElementCompassTree() => new()
    {
        CNode("线型", "═", "仅线段可换形状（含起止端点）", 92, 150, 210, children: new[]
        {
            CNode("改直线", "─", "转换为直线", 208, 92, 72, CompassAction.ElemLine),
            CNode("改波浪", "∿", "转换为波浪线", 72, 152, 196, CompassAction.ElemWave),
            CNode("改双线", "═", "转换为双线", 132, 92, 196, CompassAction.ElemDouble)
        }),
        CNode("颜色", "◐", "修改元素颜色", 208, 120, 190, children: ColorLeaves()),
        CNode("透明", "◑", "修改元素不透明度", 150, 180, 210, children: OpacityLeaves()),
        CNode("粗细", "↕", "调整元素笔触粗细", 96, 140, 168, children: new[]
        {
            CNode("加粗", "＋", "笔触加粗", 128, 168, 200, CompassAction.ElemThicker),
            CNode("变细", "－", "笔触变细", 128, 168, 200, CompassAction.ElemThinner)
        }),
        CNode("操作", "⚙", "复制、删除与退出", 168, 156, 96, children: new[]
        {
            CNode("复制", "⧉", "复制该元素", 92, 176, 210, CompassAction.DuplicateElement),
            CNode("删除", "✕", "删除该元素", 216, 82, 82, CompassAction.DeleteElement),
            CNode("退出", "⏻", "退出工具", 150, 150, 160, CompassAction.ExitTool)
        })
    };

    private ModifierKeys CompassModifiers => App.Settings.CompassModifier switch
    {
        CompassModifier.Shift => ModifierKeys.Shift,
        CompassModifier.Alt => ModifierKeys.Alt,
        CompassModifier.CtrlShift => ModifierKeys.Control | ModifierKeys.Shift,
        CompassModifier.CtrlAlt => ModifierKeys.Control | ModifierKeys.Alt,
        CompassModifier.ShiftAlt => ModifierKeys.Shift | ModifierKeys.Alt,
        _ => ModifierKeys.Control
    };

    private MouseButton CompassMouseButton => App.Settings.CompassButton == CompassButton.Middle
        ? MouseButton.Middle : MouseButton.Right;

    private bool IsCompassTrigger(MouseButtonEventArgs e) =>
        e.ChangedButton == CompassMouseButton && Keyboard.Modifiers == CompassModifiers;

    private List<CompassNode> ActiveCompassTree => _compassContext switch
    {
        CompassContext.Canvas => CanvasCompassTree,
        CompassContext.Element => ElementCompassTree,
        _ => ImageCompassTree
    };

    private bool HandleCompassDown(MouseButtonEventArgs e)
    {
        if (!IsCompassTrigger(e)) return false;
        var root = e.GetPosition(Root);
        var vp = e.GetPosition(ImageViewport);

        // Pick context from where the pointer is: over an editable element -> element menu,
        // over the picture -> ink menu, otherwise -> canvas/file menu.
        _compassContext = ResolveCompassContext(vp);
        if (_compassContext == CompassContext.Element && _selectedIndex >= 0)
        {
            // keep current selection
        }
        OpenCompass(root, vp);
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    private CompassContext ResolveCompassContext(Point viewportPoint)
    {
        // Directly over any drawn element -> element edit menu; select it for editing.
        if (HitTestAnyElement(viewportPoint, out var kind, out var index))
        {
            SelectElement(kind, index);
            return CompassContext.Element;
        }
        if (_sourcePixels != null && IsInsideImage(viewportPoint))
            return CompassContext.Image;
        return CompassContext.Canvas;
    }

    private bool HitTestAnyElement(Point vp, out ElementKind kind, out int index)
    {
        var radius = Math.Max(12, _annotationSize * 2);
        for (var i = _textCards.Count - 1; i >= 0; i--)
            if (_textCards[i].HitTest(vp, ToViewportSpace, ImageView.ActualWidth, ImageView.ActualHeight, radius))
            { kind = ElementKind.Text; index = i; return true; }
        for (var i = _fills.Count - 1; i >= 0; i--)
            if (_fills[i].HitTest(vp, ToViewportSpace, radius))
            { kind = ElementKind.Fill; index = i; return true; }
        for (var i = _annotations.Count - 1; i >= 0; i--)
            if (_annotations[i].HitTest(vp, ToViewportSpace, radius))
            { kind = ElementKind.Stroke; index = i; return true; }
        kind = ElementKind.None;
        index = -1;
        return false;
    }

    private bool IsInsideImage(Point viewportPoint)
    {
        var left = double.IsNaN(Canvas.GetLeft(ImageView)) ? 0 : Canvas.GetLeft(ImageView);
        var top = double.IsNaN(Canvas.GetTop(ImageView)) ? 0 : Canvas.GetTop(ImageView);
        return viewportPoint.X >= left && viewportPoint.X <= left + ImageView.ActualWidth &&
               viewportPoint.Y >= top && viewportPoint.Y <= top + ImageView.ActualHeight;
    }

    private bool HandleCompassMove(MouseEventArgs e)
    {
        if (!_compassOpen) return false;
        UpdateCompass(e.GetPosition(Root));
        e.Handled = true;
        return true;
    }

    private bool HandleCompassUp(MouseButtonEventArgs e)
    {
        if (!_compassOpen) return false;
        UpdateCompass(e.GetPosition(Root));

        // Trigger strictly by the pointer's active ring/sector — the guide line is decorative.
        var ring = _compassActiveRing;
        if (ring >= 0 && ring < _compassSelection.Length)
        {
            var index = _compassSelection[ring];
            if (index >= 0)
            {
                var node = CompassNodeAt(ring, index);
                if (node is { IsGroup: false }) InvokeCompass(node);
            }
        }
        CloseCompass();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        return true;
    }

    private void OpenCompass(Point rootPoint, Point imagePoint)
    {
        CloseCompass();
        _compassOrigin = rootPoint;
        _compassPointer = rootPoint;
        _compassSelection = new int[CompassRingInner.Length];
        for (var i = 0; i < _compassSelection.Length; i++) _compassSelection[i] = -1;
        _compassOpen = true;
        _draftPointer = imagePoint;

        // Guide line: decorative only. Thin, elegant, drawn above all sectors.
        _compassGuideHalo = new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
            StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        _compassGuideFlow = new Line
        {
            StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { 4, 5 },
            IsHitTestVisible = false
        };
        _compassGuideFlow.Effect = new DropShadowEffect
        {
            Color = Color.FromRgb(255, 214, 92), BlurRadius = 6, ShadowDepth = 0, Opacity = 0.85
        };
        var flowBrush = new SolidColorBrush(Color.FromRgb(255, 214, 92));
        _compassGuideFlow.Stroke = flowBrush;
        flowBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            From = Color.FromRgb(255, 214, 92), To = Color.FromRgb(96, 226, 255),
            Duration = TimeSpan.FromMilliseconds(650), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
        });
        _compassGuideFlow.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(480)) { RepeatBehavior = RepeatBehavior.Forever });

        _compassTooltip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 12, 15, 22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 8, 5), IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        _compassTooltipText = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 210
        };
        _compassTooltip.Child = _compassTooltipText;

        RebuildCompass();
        UpdateCompass(rootPoint);
    }

    /// <summary>
    /// Rebuild sectors for the current active path. Three visual states:
    /// idle (no selection -> uniform), hovered ring (active sector brightened, others dimmed),
    /// and sticky ancestors (breadcrumb path kept but dimmed).
    /// </summary>
    private void RebuildCompass()
    {
        foreach (var item in _compassVisuals) CompassLayer.Children.Remove(item);
        _compassVisuals.Clear();

        var anyActive = _compassSelection.Any(i => i >= 0);

        // Hub with context label.
        var hubLabel = _compassContext switch
        {
            CompassContext.Canvas => "画布",
            CompassContext.Element => "元素",
            _ => "标注"
        };
        var hub = new Ellipse
        {
            Width = CompassRingInner[0] * 2 - 6, Height = CompassRingInner[0] * 2 - 6,
            Fill = new SolidColorBrush(Color.FromArgb(246, 18, 21, 28)),
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 1.2, IsHitTestVisible = false
        };
        hub.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.6 };
        Canvas.SetLeft(hub, _compassOrigin.X - (CompassRingInner[0] - 3));
        Canvas.SetTop(hub, _compassOrigin.Y - (CompassRingInner[0] - 3));
        CompassLayer.Children.Add(hub);
        _compassVisuals.Add(hub);
        AddCompassText(hubLabel, _compassOrigin.X - 26, _compassOrigin.Y - 9, 52, 18, 11, Brushes.White, FontWeights.SemiBold);

        for (var ring = 0; ring < CompassRingInner.Length; ring++)
        {
            if (ring > 0 && _compassSelection[ring - 1] < 0) break;
            var nodes = CompassChildrenAt(ring);
            if (nodes.Count == 0) break;
            var span = 360.0 / nodes.Count;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var start = i * span - 90 + 1.2;
                var end = (i + 1) * span - 90 - 1.2;
                var isActive = _compassSelection[ring] == i && ring == _compassActiveRing;
                var isBreadcrumb = _compassSelection[ring] == i && ring != _compassActiveRing;
                var dim = anyActive && !isActive && !isBreadcrumb;
                var path = CreateSectorPath(_compassOrigin, CompassRingInner[ring], CompassRingOuter[ring],
                    start, end, node.Accent, isActive, isBreadcrumb, dim);
                CompassLayer.Children.Add(path);
                _compassVisuals.Add(path);

                var mid = (start + end) / 2;
                var radius = (CompassRingInner[ring] + CompassRingOuter[ring]) / 2;
                var labelPoint = Polar(_compassOrigin, radius, mid);
                var glyphSize = ring == 0 ? 14 : 12;
                var textBrush = isActive
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromArgb((byte)(dim ? 120 : 235), 235, 240, 248));
                AddCompassText(node.Glyph, labelPoint.X - 15, labelPoint.Y - 17, 30, 18, glyphSize, textBrush, FontWeights.SemiBold);
                if (ring <= 1)
                    AddCompassText(node.Label, labelPoint.X - 24, labelPoint.Y - 1, 48, 14, 9,
                        new SolidColorBrush(Color.FromArgb((byte)(dim ? 105 : 232), 235, 240, 248)), FontWeights.Normal);
            }
        }

        if (_compassTooltip != null) { CompassLayer.Children.Add(_compassTooltip); _compassVisuals.Add(_compassTooltip); }
        if (_compassGuideHalo != null) { CompassLayer.Children.Add(_compassGuideHalo); _compassVisuals.Add(_compassGuideHalo); }
        if (_compassGuideFlow != null) { CompassLayer.Children.Add(_compassGuideFlow); _compassVisuals.Add(_compassGuideFlow); }
        UpdateCompassGuide();
    }

    private void AddCompassText(string text, double x, double y, double w, double h, double size, System.Windows.Media.Brush brush, FontWeight weight)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = brush,
            Width = w, Height = h, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        CompassLayer.Children.Add(label);
        _compassVisuals.Add(label);
    }

    private List<CompassNode> CompassChildrenAt(int ring)
    {
        var nodes = ActiveCompassTree;
        for (var i = 0; i < ring; i++)
        {
            var index = _compassSelection[i];
            if (index < 0 || index >= nodes.Count) return new List<CompassNode>();
            nodes = nodes[index].Children;
        }
        return nodes;
    }

    private CompassNode? CompassNodeAt(int ring, int index)
    {
        var nodes = CompassChildrenAt(ring);
        return index >= 0 && index < nodes.Count ? nodes[index] : null;
    }

    private Path CreateSectorPath(Point center, double inner, double outer, double start, double end,
        Color accent, bool active, bool breadcrumb, bool dim)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Polar(center, inner, start), true, true);
            ctx.LineTo(Polar(center, outer, start), true, true);
            ctx.ArcTo(Polar(center, outer, end), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(Polar(center, inner, end), true, true);
            ctx.ArcTo(Polar(center, inner, start), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, true);
        }

        byte a = active ? (byte)255 : breadcrumb ? (byte)205 : dim ? (byte)92 : accent.A;
        var boost = active ? 40 : breadcrumb ? 16 : 0;
        var fade = dim ? 0.55 : 1.0;
        Color fill;
        if (active)
            fill = Color.FromArgb(a, (byte)Math.Min(255, accent.R + boost), (byte)Math.Min(255, accent.G + boost), (byte)Math.Min(255, accent.B + boost));
        else
            fill = Color.FromArgb(a,
                (byte)(accent.R * fade), (byte)(accent.G * fade), (byte)(accent.B * fade));
        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(active ? Colors.White : Color.FromArgb((byte)(dim ? 90 : 180), 255, 255, 255)),
            StrokeThickness = active ? 2 : 0.9,
            IsHitTestVisible = false
        };
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static Point Polar(Point center, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(center.X + Math.Cos(rad) * radius, center.Y + Math.Sin(rad) * radius);
    }

    /// <summary>
    /// Pointer-only selection. The line is never consulted. Sticky behaviour: once an outer
    /// ring is open, drifting beyond the compass keeps it; only returning to an inner ring
    /// collapses the deeper rings.
    /// </summary>
    private void UpdateCompass(Point pointer)
    {
        if (!_compassOpen) return;
        _compassPointer = pointer;
        var dx = pointer.X - _compassOrigin.X;
        var dy = pointer.Y - _compassOrigin.Y;
        var distance = Hypot(dx, dy);

        // Hub: collapse everything back to the first ring.
        if (distance < CompassRingInner[0] - 4)
        {
            var changed = false;
            for (var r = 0; r < _compassSelection.Length; r++)
                if (_compassSelection[r] != -1) { _compassSelection[r] = -1; changed = true; }
            _compassActiveRing = -1;
            if (changed) RebuildCompass();
            UpdateCompassGuide();
            UpdateCompassTooltip();
            return;
        }

        // Find the ring the pointer actually sits in (pointer position only).
        var pointerRing = -1;
        for (var r = 0; r < CompassRingInner.Length; r++)
        {
            if (distance >= CompassRingInner[r] - 3 && distance <= CompassRingOuter[r] + 6) { pointerRing = r; break; }
        }
        if (pointerRing < 0)
        {
            // Drifted beyond the compass: keep the current expanded path (sticky); only move the line.
            _compassActiveRing = -1;
            UpdateCompassGuide();
            UpdateCompassTooltip();
            return;
        }

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
        if (angle < 0) angle += 360;

        // Collapse rings deeper than the pointer's ring only when the pointer moved inward.
        var changed2 = false;
        if (_compassActiveRing >= 0 && pointerRing < _compassActiveRing)
        {
            for (var r = pointerRing + 1; r < _compassSelection.Length; r++)
                if (_compassSelection[r] != -1) { _compassSelection[r] = -1; changed2 = true; }
        }
        _compassActiveRing = pointerRing;

        var nodes = CompassChildrenAt(pointerRing);
        if (nodes.Count > 0)
        {
            var idx = (int)(angle / (360.0 / nodes.Count)) % nodes.Count;
            if (_compassSelection[pointerRing] != idx)
            {
                _compassSelection[pointerRing] = idx;
                // Selecting a different node invalidates that branch's children.
                for (var r = pointerRing + 1; r < _compassSelection.Length; r++)
                    if (_compassSelection[r] != -1) { _compassSelection[r] = -1; changed2 = true; }
                changed2 = true;
            }
        }
        if (changed2) RebuildCompass();
        UpdateCompassGuide();
        UpdateCompassTooltip();
    }

    private void UpdateCompassGuide()
    {
        if (_compassGuideHalo == null || _compassGuideFlow == null) return;
        foreach (var line in new[] { _compassGuideHalo, _compassGuideFlow })
        {
            line.X1 = _compassOrigin.X;
            line.Y1 = _compassOrigin.Y;
            line.X2 = _compassPointer.X;
            line.Y2 = _compassPointer.Y;
        }
    }

    private void UpdateCompassTooltip()
    {
        if (_compassTooltip == null || _compassTooltipText == null) return;
        CompassNode? node = null;
        for (var r = _compassSelection.Length - 1; r >= 0; r--)
        {
            var index = _compassSelection[r];
            if (index < 0) continue;
            node = CompassNodeAt(r, index);
            if (node != null) break;
        }
        if (node == null)
        {
            _compassTooltip.Visibility = Visibility.Collapsed;
            return;
        }
        _compassTooltipText.Text = $"{node.Label}  —  {node.Description}";
        _compassTooltip.Visibility = Visibility.Visible;
        var x = Math.Min(_compassPointer.X + 18, CompassLayer.ActualWidth - 220);
        var y = Math.Min(_compassPointer.Y + 18, CompassLayer.ActualHeight - 56);
        Canvas.SetLeft(_compassTooltip, Math.Max(4, x));
        Canvas.SetTop(_compassTooltip, Math.Max(4, y));
        _compassTooltip.UpdateLayout();
    }

    private void CloseCompass()
    {
        _compassOpen = false;
        _compassActiveRing = -1;
        for (var i = 0; i < _compassSelection.Length; i++) _compassSelection[i] = -1;
        _compassGuideHalo = null;
        _compassGuideFlow = null;
        _compassTooltip = null;
        _compassTooltipText = null;
        foreach (var item in _compassVisuals) CompassLayer.Children.Remove(item);
        _compassVisuals.Clear();
    }

    private void InvokeCompass(CompassNode node)
    {
        switch (node.Action)
        {
            // Ink tools
            case CompassAction.Pointer: SetAnnotationTool(AnnotationTool.Pointer); break;
            case CompassAction.Move: SetAnnotationTool(AnnotationTool.Move); break;
            case CompassAction.Line: SetAnnotationTool(AnnotationTool.Line); break;
            case CompassAction.Wave: SetAnnotationTool(AnnotationTool.Wave); break;
            case CompassAction.DoubleLine: SetAnnotationTool(AnnotationTool.DoubleLine); break;
            case CompassAction.Freehand: SetAnnotationTool(AnnotationTool.Freehand); break;
            case CompassAction.Circle: SetAnnotationTool(AnnotationTool.Circle); break;
            case CompassAction.Fill: SetAnnotationTool(AnnotationTool.Fill); break;
            case CompassAction.EraserFree: SetAnnotationTool(AnnotationTool.EraserFree); break;
            case CompassAction.EraserClick: SetAnnotationTool(AnnotationTool.EraserClick); break;
            case CompassAction.EraserBigger: _eraserSize = Math.Min(120, _eraserSize + 6); break;
            case CompassAction.EraserSmaller: _eraserSize = Math.Max(4, _eraserSize - 6); break;
            case CompassAction.Size1: _annotationSize = 1; break;
            case CompassAction.Size2: _annotationSize = 2; break;
            case CompassAction.Size3: _annotationSize = 4; break;
            case CompassAction.Size4: _annotationSize = 7; break;
            case CompassAction.Size5: _annotationSize = 11; break;
            case CompassAction.SetColor:
                if (node.TargetColor.HasValue) _annotationColor = node.TargetColor.Value;
                ApplyColorToSelection();
                break;
            case CompassAction.OpenPalette: OpenColorPalette(); break;
            case CompassAction.ColorCycle:
                _annotationColor = _annotationColor == Color.FromRgb(240, 72, 72) ? Color.FromRgb(236, 200, 56)
                    : _annotationColor == Color.FromRgb(236, 200, 56) ? Color.FromRgb(64, 150, 246)
                    : _annotationColor == Color.FromRgb(64, 150, 246) ? Color.FromRgb(80, 206, 108)
                    : Color.FromRgb(240, 72, 72);
                break;
            case CompassAction.OpacityHighlight: SetOpacity(0.35, true); break;
            case CompassAction.OpacityHalf: SetOpacity(0.55, false); break;
            case CompassAction.OpacitySolid: SetOpacity(1.0, false); break;
            case CompassAction.HighlighterToggle: SetOpacity(_annotationOpacity, !_annotationHighlighter); break;
            case CompassAction.RegionTranslate: BeginRegionAction(RegionAction.Translate); break;
            case CompassAction.RegionOcr: BeginRegionAction(RegionAction.Ocr); break;
            case CompassAction.ClearAll: ClearAllAnnotations(); break;

            // Canvas
            case CompassAction.OpenImage: OpenImageDialog(); break;
            case CompassAction.SaveImage: SaveImage(false); break;
            case CompassAction.SaveAs: SaveImage(true); break;
            case CompassAction.Screenshot: StartScreenshot(); break;
            case CompassAction.ToggleTopMost: ExecuteHotkey(HotkeyAction.ToggleTopMost); break;
            case CompassAction.Settings: App.ShowSettings(); break;
            case CompassAction.About: App.ShowAbout(); break;
            case CompassAction.ExitTool: ExitTool(); break;

            // Element
            case CompassAction.DeleteElement: DeleteSelectedElement(); break;
            case CompassAction.DuplicateElement: DuplicateSelectedElement(); break;
            case CompassAction.ElemLine: ChangeSelectedLineType(AnnotationTool.Line); break;
            case CompassAction.ElemWave: ChangeSelectedLineType(AnnotationTool.Wave); break;
            case CompassAction.ElemDouble: ChangeSelectedLineType(AnnotationTool.DoubleLine); break;
            case CompassAction.ElemThicker: ChangeSelectedThickness(2); break;
            case CompassAction.ElemThinner: ChangeSelectedThickness(-2); break;
        }
        ShowToolBadge();
    }

    private void ExitTool()
    {
        SetAnnotationTool(AnnotationTool.Pointer);
        ClearElementSelection();
    }
}
