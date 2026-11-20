using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using WBImageViewer.Models;
using WBImageViewer.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace WBImageViewer;

/// <summary>
/// Annotation drawing, element editing (endpoints / bounds / move / delete), paint bucket,
/// opacity &amp; highlighter, region translate/OCR, .gjpg persistence and badge/cursor UI.
/// </summary>
public partial class MainWindow
{
    private void SetAnnotationTool(AnnotationTool tool)
    {
        _annotationTool = tool;
        SetToolCursor();
    }

    private void SetToolCursor()
    {
        Cursor = _annotationTool switch
        {
            AnnotationTool.Pointer => Cursors.Arrow,
            AnnotationTool.Move => Cursors.SizeAll,
            AnnotationTool.Line or AnnotationTool.Wave or AnnotationTool.DoubleLine or
                AnnotationTool.Freehand or AnnotationTool.Circle or AnnotationTool.Fill => CursorFactory.Pen,
            AnnotationTool.EraserFree or AnnotationTool.EraserClick => CursorFactory.Eraser,
            _ => Cursors.Arrow
        };
    }

    private void RestoreCursor() => SetToolCursor();

    private static string ToolName(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Line => "直线",
        AnnotationTool.Wave => "波浪线",
        AnnotationTool.DoubleLine => "双线",
        AnnotationTool.Freehand => "自由画",
        AnnotationTool.Circle => "圈注",
        AnnotationTool.Fill => "油漆桶填充",
        AnnotationTool.EraserFree => "自由擦除",
        AnnotationTool.EraserClick => "点击擦除",
        AnnotationTool.Move => "移动元素",
        _ => string.Empty
    };

    private string SelectedElementName() => _selectedKind switch
    {
        ElementKind.Stroke when _selectedIndex >= 0 && _selectedIndex < _annotations.Count
            => ToolName(_annotations[_selectedIndex].Tool),
        ElementKind.Fill => "填充",
        ElementKind.Text => "文字卡片",
        _ => string.Empty
    };

    private void ShowToolBadge()
    {
        var isSelection = _selectedKind != ElementKind.None;
        var name = _regionAction != RegionAction.None
            ? (_regionAction == RegionAction.Ocr ? "框选识别" : "框选翻译")
            : isSelection ? SelectedElementName() : ToolName(_annotationTool);
        var show = !string.IsNullOrEmpty(name);
        if (!show)
        {
            ToolBadge.Visibility = Visibility.Collapsed;
            ToolBadgeMenu.Visibility = Visibility.Collapsed;
            _badgeMenuKind = BadgeMenuKind.None;
            return;
        }

        var selectedStroke = isSelection && _selectedKind == ElementKind.Stroke &&
                             _selectedIndex >= 0 && _selectedIndex < _annotations.Count
            ? _annotations[_selectedIndex] : null;
        var diameter = selectedStroke != null ? selectedStroke.Thickness * ViewScale
            : _annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick
                ? _eraserSize : _annotationSize;
        var style = (selectedStroke?.Style ?? _annotationLineStyle) == InkStrokeStyle.Standard ? "标准" : "手绘";
        var opacity = selectedStroke?.Opacity ?? _annotationOpacity;
        var highlighter = selectedStroke?.Highlighter ?? _annotationHighlighter;
        var color = selectedStroke?.Color ?? _annotationColor;

        ToolBadgeName.Content = isSelection ? $"已选中 · {name}" : $"标注：{name} · {style}";
        ToolBadgeSize.Content = $"直径 {diameter:0}";
        ToolBadgeOpacity.Content = highlighter ? "高亮" : $"{opacity:0%}";
        ToolBadgeColorDot.Fill = new SolidColorBrush(color);
        ToolBadge.Visibility = Visibility.Visible;
    }

    // ---------------- annotation drawing ----------------

    private bool HandleAnnotationDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return false;
        var vp = e.GetPosition(ImageViewport);

        // Region selection (translate / OCR) takes priority.
        if (_regionAction != RegionAction.None)
        {
            _regionSelecting = true;
            _regionStartViewport = vp;
            _regionEndViewport = vp;
            CaptureMouse();
            e.Handled = true;
            return true;
        }

        // Element editing: click on an existing element in Pointer/Move mode selects it;
        // dragging a handle resizes; dragging the body moves.
        if (_annotationTool is AnnotationTool.Pointer or AnnotationTool.Move)
        {
            if (TryBeginElementEdit(vp))
            {
                e.Handled = true;
                return true;
            }
            if (_annotationTool == AnnotationTool.Pointer)
            {
                ClearElementSelection();
                ShowToolBadge();
            }
            return false;
        }

        if (_sourcePixels == null) return false;

        if (_annotationTool == AnnotationTool.Fill)
        {
            if (!IsInsideImage(vp)) return false;
            var seed = ToImageSpace(vp);
            var fill = new AnnotationFill { Seed = seed, Color = _annotationColor, Opacity = _annotationOpacity };
            fill.Build(_sourcePixels, _pixelWidth, _pixelHeight);
            _fills.Add(fill);
            RedrawAnnotations();
            e.Handled = true;
            return true;
        }

        if (_annotationTool == AnnotationTool.EraserClick)
        {
            EraseAt(vp, Math.Max(_eraserSize, 10));
            e.Handled = true;
            return true;
        }

        _annotating = true;
        _draftSeed = Math.Abs(Guid.NewGuid().GetHashCode()) % int.MaxValue + 1;
        _draftPoints = new List<Point> { ToImageSpace(vp) };
        _draftPointer = vp;
        RefreshDraftVisual();
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    private bool HandleAnnotationMove(MouseEventArgs e)
    {
        if (_regionSelecting)
        {
            _regionEndViewport = e.GetPosition(ImageViewport);
            RefreshRegionVisual();
            e.Handled = true;
            return true;
        }

        if (_editingElement)
        {
            var current = ToImageSpace(e.GetPosition(ImageViewport));
            ApplyElementEdit(current);
            e.Handled = true;
            return true;
        }

        if (_movingAnnotation >= 0 && _moveOriginalPoints != null)
        {
            var current = ToImageSpace(e.GetPosition(ImageViewport));
            var dx = current.X - _moveStartImage.X;
            var dy = current.Y - _moveStartImage.Y;
            var points = _annotations[_movingAnnotation].Points;
            for (var i = 0; i < points.Count; i++)
                points[i] = new Point(_moveOriginalPoints[i].X + dx, _moveOriginalPoints[i].Y + dy);
            RedrawAnnotations();
            e.Handled = true;
            return true;
        }

        if (!_annotating) return false;
        var point = e.GetPosition(ImageViewport);
        _draftPointer = point;

        if (_annotationTool == AnnotationTool.EraserFree)
        {
            EraseAt(point, _eraserSize);
            e.Handled = true;
            return true;
        }

        var normalized = ToImageSpace(point);
        var last = _draftPoints[^1];
        var lastViewport = ToViewportSpace(last);
        if (Hypot(point.X - lastViewport.X, point.Y - lastViewport.Y) >= 1.5)
        {
            _draftPoints.Add(normalized);
            RefreshDraftVisual();
        }
        e.Handled = true;
        return true;
    }

    private bool HandleAnnotationUp(MouseButtonEventArgs e)
    {
        if (_regionSelecting)
        {
            _regionSelecting = false;
            ClearRegionVisual();
            if (IsMouseCaptured) ReleaseMouseCapture();
            var rect = new Rect(_regionStartViewport, _regionEndViewport);
            if (rect.Width > 8 && rect.Height > 8 && _sourcePixels != null)
                _ = RunRegionActionAsync(rect);
            e.Handled = true;
            return true;
        }

        if (_editingElement)
        {
            _editingElement = false;
            _editingHandle = -1;
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
            return true;
        }

        if (_movingAnnotation >= 0)
        {
            _movingAnnotation = -1;
            _moveOriginalPoints = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
            return true;
        }

        if (!_annotating) return false;
        _annotating = false;
        ClearDraftVisual();

        if (_annotationTool != AnnotationTool.EraserFree && _draftPoints.Count >= 2)
        {
            var stroke = new AnnotationStroke(_annotationTool, _draftPoints, _annotationColor,
                ScreenToImageThickness(_annotationSize), _annotationLineStyle, _draftSeed)
            {
                Opacity = _annotationOpacity,
                Highlighter = _annotationHighlighter
            };
            _annotations.Add(stroke);
            RedrawAnnotations();
        }
        _draftPoints.Clear();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        return true;
    }

    // ---------------- element selection & editing ----------------

    private bool HitTestSelectedElement(Point vp)
    {
        var radius = Math.Max(12, _annotationSize * 2);
        return _selectedKind switch
        {
            ElementKind.Stroke when _selectedIndex >= 0 && _selectedIndex < _annotations.Count =>
                _annotations[_selectedIndex].HitTest(vp, ToViewportSpace, radius, ViewScale, Math.Max(1, _pixelWidth), Math.Max(1, _pixelHeight)),
            ElementKind.Fill when _selectedIndex >= 0 && _selectedIndex < _fills.Count =>
                _fills[_selectedIndex].HitTest(vp, ToViewportSpace, radius),
            ElementKind.Text when _selectedIndex >= 0 && _selectedIndex < _textCards.Count =>
                _textCards[_selectedIndex].HitTest(vp, ToViewportSpace, ImageView.ActualWidth, ImageView.ActualHeight, radius),
            _ => false
        };
    }

    private bool TryBeginElementEdit(Point vp)
    {
        var handleRadius = 11;
        // First: if an element is selected, check its handles.
        if (_selectedKind == ElementKind.Stroke && _selectedIndex >= 0 && _selectedIndex < _annotations.Count)
        {
            var stroke = _annotations[_selectedIndex];
            var handles = stroke.GetHandles().Select(h => ToViewportSpace(h.Value)).ToList();
            for (var i = 0; i < handles.Count; i++)
            {
                if (Hypot(vp.X - handles[i].X, vp.Y - handles[i].Y) <= handleRadius)
                {
                    _editingElement = true;
                    _editingHandle = i;
                    _editStartImage = ToImageSpace(vp);
                    _editOriginalPoints = stroke.Points.ToList();
                    _editOriginalHandleValue = stroke.GetHandleValue(i);
                    var bounds = stroke.GetHandles();
                    _editOriginalTopLeft = bounds.Count > 0 ? bounds[0].Value : new Point(0, 0);
                    _editOriginalBottomRight = bounds.Count > 1 ? bounds[1].Value : new Point(1, 1);
                    CaptureMouse();
                    return true;
                }
            }
            if (stroke.HitTest(vp, ToViewportSpace, Math.Max(10, stroke.Thickness * ViewScale), ViewScale, Math.Max(1, _pixelWidth), Math.Max(1, _pixelHeight)))
            {
                BeginMoveStroke(_selectedIndex, vp);
                return true;
            }
        }

        // Otherwise hit-test from topmost element down.
        for (var i = _fills.Count - 1; i >= 0; i--)
            if (_fills[i].HitTest(vp, ToViewportSpace, 12))
            {
                SelectElement(ElementKind.Fill, i);
                _movingAnnotation = -1;
                // fills are seed-based; move by dragging seed
                _editingElement = true;
                _editingHandle = 0;
                _editStartImage = ToImageSpace(vp);
                _editOriginalPoints = new List<Point> { _fills[i].Seed };
                CaptureMouse();
                return true;
            }
        for (var i = _textCards.Count - 1; i >= 0; i--)
            if (_textCards[i].HitTest(vp, ToViewportSpace, ImageView.ActualWidth, ImageView.ActualHeight, 10))
            {
                SelectElement(ElementKind.Text, i);
                _editingElement = true;
                _editingHandle = 0;
                _editStartImage = ToImageSpace(vp);
                _editOriginalPoints = new List<Point> { _textCards[i].TopLeft };
                CaptureMouse();
                return true;
            }
        for (var i = _annotations.Count - 1; i >= 0; i--)
            if (_annotations[i].HitTest(vp, ToViewportSpace, Math.Max(12, _annotationSize * 2), ViewScale, Math.Max(1, _pixelWidth), Math.Max(1, _pixelHeight)))
            {
                SelectElement(ElementKind.Stroke, i);
                BeginMoveStroke(i, vp);
                return true;
            }
        return false;
    }

    private void BeginMoveStroke(int index, Point vp)
    {
        _movingAnnotation = index;
        _moveStartImage = ToImageSpace(vp);
        _moveOriginalPoints = _annotations[index].Points.ToList();
        CaptureMouse();
    }

    private void SelectElement(ElementKind kind, int index)
    {
        _selectedKind = kind;
        _selectedIndex = index;
        RedrawAnnotations();
        ShowToolBadge();
    }

    private void ClearElementSelection()
    {
        _selectedKind = ElementKind.None;
        _selectedIndex = -1;
        RedrawAnnotations();
    }

    private void ApplyElementEdit(Point current)
    {
        var dx = current.X - _editStartImage.X;
        var dy = current.Y - _editStartImage.Y;
        switch (_selectedKind)
        {
            case ElementKind.Stroke when _selectedIndex >= 0 && _selectedIndex < _annotations.Count:
                var stroke = _annotations[_selectedIndex];
                if (_editingHandle >= 0)
                {
                    // Handle indices are logical, so use the snapshotted handle value
                    // (not Points[handleIndex]) and keep the whole edit in image space.
                    var target = new Point(_editOriginalHandleValue.X + dx,
                                           _editOriginalHandleValue.Y + dy);
                    stroke.ApplyHandle(_editingHandle, target,
                        _editOriginalTopLeft, _editOriginalBottomRight);
                }
                break;
            case ElementKind.Fill when _selectedIndex >= 0 && _selectedIndex < _fills.Count:
                var fill = _fills[_selectedIndex];
                fill.Seed = new Point(_editOriginalPoints![0].X + dx, _editOriginalPoints[0].Y + dy);
                if (_sourcePixels != null) fill.Build(_sourcePixels, _pixelWidth, _pixelHeight);
                break;
            case ElementKind.Text when _selectedIndex >= 0 && _selectedIndex < _textCards.Count:
                var card = _textCards[_selectedIndex];
                card.TopLeft = new Point(_editOriginalPoints![0].X + dx, _editOriginalPoints[0].Y + dy);
                break;
        }
        RedrawAnnotations();
    }

    private void DeleteSelectedElement()
    {
        if (_selectedIndex < 0) return;
        switch (_selectedKind)
        {
            case ElementKind.Stroke when _selectedIndex < _annotations.Count: _annotations.RemoveAt(_selectedIndex); break;
            case ElementKind.Fill when _selectedIndex < _fills.Count: _fills.RemoveAt(_selectedIndex); break;
            case ElementKind.Text when _selectedIndex < _textCards.Count: _textCards.RemoveAt(_selectedIndex); break;
        }
        ClearElementSelection();
    }

    private void DuplicateSelectedElement()
    {
        if (_selectedIndex < 0) return;
        var offset = new Point(0.02, 0.02);
        switch (_selectedKind)
        {
            case ElementKind.Stroke when _selectedIndex < _annotations.Count:
                var s = _annotations[_selectedIndex];
                var copy = new AnnotationStroke(s.Tool,
                    s.Points.Select(p => new Point(p.X + offset.X, p.Y + offset.Y)),
                    s.Color, s.Thickness, s.Style, s.Seed) { Opacity = s.Opacity, Highlighter = s.Highlighter };
                _annotations.Add(copy);
                _selectedIndex = _annotations.Count - 1;
                break;
            case ElementKind.Fill when _selectedIndex < _fills.Count:
                var f = _fills[_selectedIndex];
                var fc = new AnnotationFill { Seed = new Point(f.Seed.X + offset.X, f.Seed.Y + offset.Y),
                    Color = f.Color, Opacity = f.Opacity, Tolerance = f.Tolerance };
                if (_sourcePixels != null) fc.Build(_sourcePixels, _pixelWidth, _pixelHeight);
                _fills.Add(fc);
                _selectedIndex = _fills.Count - 1;
                break;
            case ElementKind.Text when _selectedIndex < _textCards.Count:
                var t = _textCards[_selectedIndex];
                _textCards.Add(new AnnotationText { TopLeft = new Point(t.TopLeft.X + offset.X, t.TopLeft.Y + offset.Y),
                    Width = t.Width, Source = t.Source, Text = t.Text, Kind = t.Kind, Opacity = t.Opacity });
                _selectedIndex = _textCards.Count - 1;
                break;
        }
        RedrawAnnotations();
    }

    private void SetLineStyle(InkStrokeStyle style)
    {
        _annotationLineStyle = style;
        if (_selectedKind == ElementKind.Stroke && _selectedIndex >= 0 &&
            _selectedIndex < _annotations.Count)
        {
            _annotations[_selectedIndex].Style = style;
            RedrawAnnotations();
        }
    }

    private void ChangeSelectedLineType(AnnotationTool target)
    {
        if (_selectedKind != ElementKind.Stroke || _selectedIndex < 0 || _selectedIndex >= _annotations.Count) return;
        var stroke = _annotations[_selectedIndex];
        if (!AnnotationStroke.CanChangeTo(stroke.Tool, target)) return; // circle/freehand cannot be converted
        stroke.Tool = target;
        stroke.Reseed();
        RedrawAnnotations();
    }

    private void ChangeSelectedThickness(double delta)
    {
        if (_selectedKind != ElementKind.Stroke || _selectedIndex < 0 || _selectedIndex >= _annotations.Count) return;
        var stroke = _annotations[_selectedIndex];
        stroke.Thickness = Math.Clamp(stroke.Thickness + ScreenToImageThickness(delta), 0.25, 80);
        RedrawAnnotations();
    }

    private void ApplyColorToSelection()
    {
        if (_selectedIndex < 0) return;
        switch (_selectedKind)
        {
            case ElementKind.Stroke when _selectedIndex < _annotations.Count: _annotations[_selectedIndex].Color = _annotationColor; break;
            case ElementKind.Fill when _selectedIndex < _fills.Count:
                _fills[_selectedIndex].Color = _annotationColor;
                if (_sourcePixels != null) _fills[_selectedIndex].Build(_sourcePixels, _pixelWidth, _pixelHeight);
                break;
        }
        RedrawAnnotations();
    }

    private void SetOpacity(double opacity, bool highlighter)
    {
        _annotationOpacity = opacity;
        _annotationHighlighter = highlighter;
        if (_selectedIndex < 0) return;
        switch (_selectedKind)
        {
            case ElementKind.Stroke when _selectedIndex < _annotations.Count:
                var s = _annotations[_selectedIndex];
                s.Opacity = opacity; s.Highlighter = highlighter; break;
            case ElementKind.Fill when _selectedIndex < _fills.Count:
                var f = _fills[_selectedIndex];
                f.Opacity = opacity;
                if (_sourcePixels != null) f.Build(_sourcePixels, _pixelWidth, _pixelHeight);
                break;
            case ElementKind.Text when _selectedIndex < _textCards.Count:
                _textCards[_selectedIndex].Opacity = opacity; break;
        }
        RedrawAnnotations();
    }

    private void ClearAllAnnotations()
    {
        _annotations.Clear();
        _fills.Clear();
        _textCards.Clear();
        ClearElementSelection();
        RedrawAnnotations();
    }

    private Point ToImageSpace(Point viewportPoint)
    {
        var left = double.IsNaN(Canvas.GetLeft(ImageView)) ? 0 : Canvas.GetLeft(ImageView);
        var top = double.IsNaN(Canvas.GetTop(ImageView)) ? 0 : Canvas.GetTop(ImageView);
        var width = Math.Max(1, ImageView.ActualWidth);
        var height = Math.Max(1, ImageView.ActualHeight);
        return new Point((viewportPoint.X - left) / width, (viewportPoint.Y - top) / height);
    }

    private Point ToViewportSpace(Point imagePoint)
    {
        var left = double.IsNaN(Canvas.GetLeft(ImageView)) ? 0 : Canvas.GetLeft(ImageView);
        var top = double.IsNaN(Canvas.GetTop(ImageView)) ? 0 : Canvas.GetTop(ImageView);
        return new Point(left + imagePoint.X * ImageView.ActualWidth,
            top + imagePoint.Y * ImageView.ActualHeight);
    }

    private double ViewScale => Math.Max(0.001,
        ImageView.ActualWidth / Math.Max(1.0, _pixelWidth));

    private double ScreenToImageThickness(double screenPixels) =>
        Math.Max(0.25, screenPixels / ViewScale);

    private void RedrawAnnotations()
    {
        AnnotationLayer.Children.Clear();
        var scale = ViewScale;
        var iw = Math.Max(1, _pixelWidth);
        var ih = Math.Max(1, _pixelHeight);
        for (var i = 0; i < _fills.Count; i++)
        {
            var tl = ToViewportSpace(new Point(0, 0));
            var seed = ToViewportSpace(_fills[i].Seed);
            var visual = _fills[i].CreateVisual(_selectedKind == ElementKind.Fill && _selectedIndex == i,
                new Point(seed.X - tl.X, seed.Y - tl.Y));
            if (visual == null) continue;
            Canvas.SetLeft(visual, tl.X);
            Canvas.SetTop(visual, tl.Y);
            if (visual is Canvas host)
            {
                host.Width = ImageView.ActualWidth;
                host.Height = ImageView.ActualHeight;
                if (host.Children.Count > 0 && host.Children[0] is System.Windows.Controls.Image img)
                {
                    img.Width = ImageView.ActualWidth;
                    img.Height = ImageView.ActualHeight;
                }
            }
            AnnotationLayer.Children.Add(visual);
        }
        for (var i = 0; i < _annotations.Count; i++)
            AnnotationLayer.Children.Add(_annotations[i].CreateVisual(ToViewportSpace,
                _selectedKind == ElementKind.Stroke && _selectedIndex == i, scale, iw, ih));
        for (var i = 0; i < _textCards.Count; i++)
            AnnotationLayer.Children.Add(_textCards[i].CreateVisual(ToViewportSpace, ImageView.ActualWidth,
                _selectedKind == ElementKind.Text && _selectedIndex == i, scale));
    }

    private void RefreshDraftVisual()
    {
        ClearDraftVisual();
        if (_draftPoints.Count == 0) return;
        if (_annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick)
        {
            var cursor = new Ellipse
            {
                Width = _eraserSize * 2, Height = _eraserSize * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(cursor, _draftPointer.X - _eraserSize);
            Canvas.SetTop(cursor, _draftPointer.Y - _eraserSize);
            _draftVisual = cursor;
        }
        else
        {
            var draft = new AnnotationStroke(_annotationTool, _draftPoints, _annotationColor,
                ScreenToImageThickness(_annotationSize), _annotationLineStyle, _draftSeed)
            {
                Opacity = _annotationOpacity, Highlighter = _annotationHighlighter
            };
            _draftVisual = draft.CreateVisual(ToViewportSpace, false, ViewScale,
                Math.Max(1, _pixelWidth), Math.Max(1, _pixelHeight));
        }
        AnnotationLayer.Children.Add(_draftVisual);
    }

    private void ClearDraftVisual()
    {
        if (_draftVisual != null) AnnotationLayer.Children.Remove(_draftVisual);
        _draftVisual = null;
    }

    private void EraseAt(Point viewportPoint, double radius)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
            if (_annotations[i].HitTest(viewportPoint, ToViewportSpace, radius, ViewScale, Math.Max(1, _pixelWidth), Math.Max(1, _pixelHeight)))
            {
                _annotations.RemoveAt(i);
                RedrawAnnotations();
                return;
            }
        for (var i = _fills.Count - 1; i >= 0; i--)
            if (_fills[i].HitTest(viewportPoint, ToViewportSpace, radius))
            {
                _fills.RemoveAt(i);
                RedrawAnnotations();
                return;
            }
    }

    // ---------------- region translate / OCR ----------------

    private void BeginRegionAction(RegionAction action)
    {
        if (!AiService.IsConfigured)
        {
            MessageBox.Show("尚未配置大模型。请先在后台设置中填写 API 地址、密钥与模型名称，再使用 AI 识别或翻译。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            App.ShowAiSettings();
            return;
        }
        _regionAction = action;
        SetGrabCursor();
        ShowToolBadge();
    }

    private void RefreshRegionVisual()
    {
        ClearRegionVisual();
        var rect = new Rect(_regionStartViewport, _regionEndViewport);
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 90, 160, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(120, 190, 255)),
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(border, rect.Left);
        Canvas.SetTop(border, rect.Top);
        border.Width = rect.Width;
        border.Height = rect.Height;
        _regionVisual = border;
        AnnotationLayer.Children.Add(border);
    }

    private void ClearRegionVisual()
    {
        if (_regionVisual != null) AnnotationLayer.Children.Remove(_regionVisual);
        _regionVisual = null;
    }

    private async Task RunRegionActionAsync(Rect viewportRect)
    {
        var action = _regionAction;
        _regionAction = RegionAction.None;
        RestoreCursor();
        if (!AiService.IsConfigured)
        {
            MessageBox.Show("请先在后台设置中配置大模型 API（地址、密钥、模型），才能使用翻译与文字识别。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var crop = CropViewport(viewportRect);
            var result = action == RegionAction.Ocr
                ? await AiService.OcrAsync(crop)
                : await AiService.TranslateImageAsync(crop);
            if (string.IsNullOrWhiteSpace(result)) return;
            var imgPoint = ToImageSpace(new Point(viewportRect.Left, viewportRect.Bottom));
            var card = new AnnotationText
            {
                TopLeft = imgPoint,
                Width = Math.Clamp(viewportRect.Width / Math.Max(1, ImageView.ActualWidth), 0.2, 0.6),
                Source = "截图",
                Text = result.Trim(),
                Kind = action == RegionAction.Ocr ? "ocr" : "translate"
            };
            _textCards.Add(card);
            SelectElement(ElementKind.Text, _textCards.Count - 1);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"AI 处理失败：{ex.Message}", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private BitmapSource CropViewport(Rect rect)
    {
        var imgLeft = double.IsNaN(Canvas.GetLeft(ImageView)) ? 0 : Canvas.GetLeft(ImageView);
        var imgTop = double.IsNaN(Canvas.GetTop(ImageView)) ? 0 : Canvas.GetTop(ImageView);
        var scale = _pixelWidth / Math.Max(1.0, ImageView.ActualWidth);
        var sx = Math.Clamp((int)Math.Round((rect.Left - imgLeft) * scale), 0, Math.Max(0, _pixelWidth - 1));
        var sy = Math.Clamp((int)Math.Round((rect.Top - imgTop) * scale), 0, Math.Max(0, _pixelHeight - 1));
        var sw = Math.Clamp((int)Math.Round(rect.Width * scale), 1, _pixelWidth - sx);
        var sh = Math.Clamp((int)Math.Round(rect.Height * scale), 1, _pixelHeight - sy);
        var cropped = new CroppedBitmap(_display!, new Int32Rect(sx, sy, sw, sh));
        return new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
    }

    // ---------------- palette ----------------

    private void OpenColorPalette()
    {
        var dialog = new Views.ColorPaletteWindow { Owner = this, SelectedColor = _annotationColor };
        if (dialog.ShowDialog() == true)
        {
            _annotationColor = dialog.SelectedColor;
            ApplyColorToSelection();
            ShowToolBadge();
        }
    }

    // ---------------- .gjpg persistence ----------------

    private void OpenImageDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片与工程文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff;*.gjpg|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true) App.OpenDocument(dialog.FileName);
    }

    private void SaveImage(bool saveAs)
    {
        if (_sourcePixels == null) return;
        string path;
        if (saveAs || string.IsNullOrEmpty(_currentImagePath) ||
            !_currentImagePath.EndsWith(GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "怪叔叔看图工程 (*.gjpg)|*.gjpg",
                FileName = string.IsNullOrEmpty(_currentProjectName) ? "标注图片.gjpg" : _currentProjectName,
                DefaultExt = GjpgService.Extension
            };
            if (dialog.ShowDialog() == true) path = dialog.FileName;
            else return;
        }
        else path = _currentImagePath;

        try
        {
            var source = _display ?? ImageView.Source as BitmapSource;
            if (source == null) return;
            GjpgService.Save(path, source, System.IO.Path.GetFileName(_currentImagePath),
                _annotations, _fills, _textCards, _adjustments);
            _currentImagePath = path;
            _currentProjectName = System.IO.Path.GetFileName(path);
            Title = $"{AppInfo.Name} - {_currentProjectName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Load a .gjpg project, restoring image plus all editable traces.</summary>
    public bool LoadProject(string path)
    {
        if (!GjpgService.TryLoad(path, out var image, out var imageName, out var strokes,
                out var fills, out var texts, out var adjustments))
            return false;

        ClosePdfDocument();
        var source = image.Format == PixelFormats.Bgra32
            ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        _pixelWidth = source.PixelWidth;
        _pixelHeight = source.PixelHeight;
        _sourcePixels = new int[_pixelWidth * _pixelHeight];
        source.CopyPixels(_sourcePixels, _pixelWidth * 4, 0);
        _displayPixels = new int[_sourcePixels.Length];
        _display = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        _zoom = 1;
        _offsetX = _offsetY = 0;

        _annotations.Clear();
        _annotations.AddRange(strokes);
        _fills.Clear();
        _fills.AddRange(fills);
        foreach (var f in _fills) if (_sourcePixels != null) f.Build(_sourcePixels, _pixelWidth, _pixelHeight);
        _textCards.Clear();
        _textCards.AddRange(texts);
        ClearElementSelection();
        ClearDraftVisual();

        _adjustments = adjustments ?? new ImageAdjustments();
        UpdateAdjustmentControls();
        ApplyAdjustments();
        ImageView.Source = _display;
        Hint.Visibility = Visibility.Collapsed;
        _currentImagePath = path;
        _currentProjectName = imageName;
        Title = $"{AppInfo.Name} - {System.IO.Path.GetFileName(path)}";
        ApplyImagePosition();
        if (IsLoaded) FitImageToWindow();
        RedrawAnnotations();
        SyncPlaylistForCurrentImage();
        return true;
    }

    // ---------------- screenshot ----------------

    private void StartScreenshot(ScreenshotMode mode = ScreenshotMode.Region)
    {
        try
        {
            PrepareForScreenOverlay();

            // Snapshot before the overlay exists: cropping the live screen would
            // include the dimmer/selection chrome and could block the UI thread.
            var screen = Views.ScreenshotWindow.CaptureScreen();
            if (screen == null)
            {
                MessageBox.Show("无法读取屏幕内容，截图已取消。", AppInfo.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var wasTopmost = Topmost;
            Topmost = false;
            try
            {
                var shot = new Views.ScreenshotWindow(mode, screen)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    Owner = null
                };
                if (shot.ShowDialog() == true && shot.CapturedImage != null)
                    App.OpenBitmap(shot.CapturedImage, $"屏幕截图 - {mode}");
            }
            finally
            {
                Topmost = wasTopmost;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"截图功能启动失败：{ex.Message}", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Drop every transient UI grab before a modal overlay takes input.</summary>
    private void PrepareForScreenOverlay()
    {
        if (_compassOpen) CloseCompass();
        if (_annotating) { _annotating = false; ClearDraftVisual(); }
        _panningImage = false;
        _movingWindow = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        _badgeMenuKind = BadgeMenuKind.None;
        ToolBadgeMenu.Visibility = Visibility.Collapsed;
        ShotMenu.IsOpen = false;
        RestoreCursor();
    }

    private void ShotButton_Click(object sender, RoutedEventArgs e) => ShotMenu.IsOpen = true;
    private void ShotFull_Click(object sender, RoutedEventArgs e) => StartScreenshot(ScreenshotMode.FullScreen);
    private void ShotWindow_Click(object sender, RoutedEventArgs e) => StartScreenshot(ScreenshotMode.Window);
    private void ShotRegion_Click(object sender, RoutedEventArgs e) => StartScreenshot(ScreenshotMode.Region);
    private void ShotFree_Click(object sender, RoutedEventArgs e) => StartScreenshot(ScreenshotMode.Freeform);

    // ---------------- badge quick controls ----------------

    // Each badge segment opens only its own popup. The contents also depend on whether
    // an element is selected - e.g. circle/freehand have no "convert to" options.
    private const string BadgeTagLine = "line:";
    private const string BadgeTagStyle = "style:";
    private const string BadgeTagSize = "size:";
    private const string BadgeTagOpacity = "opacity:";
    private const string BadgeTagColor = "color:";

    private void ShowBadgeMenu(BadgeMenuKind kind)
    {
        if (_badgeMenuKind == kind && ToolBadgeMenu.Visibility == Visibility.Visible)
        {
            CloseBadgeMenu();
            return;
        }
        _badgeMenuKind = kind;
        ToolBadgeMenuItems.Children.Clear();

        var selectedStroke = _selectedKind == ElementKind.Stroke && _selectedIndex >= 0 &&
                             _selectedIndex < _annotations.Count ? _annotations[_selectedIndex] : null;
        switch (kind)
        {
            case BadgeMenuKind.LineType:
                if (selectedStroke != null)
                {
                    if (AnnotationStroke.CanChangeTo(selectedStroke.Tool, AnnotationTool.Line))
                        AddBadgeRow("切换元素类型", new[]
                        {
                            ("直线", BadgeTagLine + AnnotationTool.Line),
                            ("波浪", BadgeTagLine + AnnotationTool.Wave),
                            ("双线", BadgeTagLine + AnnotationTool.DoubleLine)
                        });
                    else
                        AddBadgeInfo($"{SelectedElementName()} 不能转换为其他形状；可调整笔触、线径、透明度与颜色。");
                }
                else
                {
                    AddBadgeRow("绘制形状", new[]
                    {
                        ("直线", BadgeTagLine + AnnotationTool.Line), ("波浪", BadgeTagLine + AnnotationTool.Wave),
                        ("双线", BadgeTagLine + AnnotationTool.DoubleLine), ("自由画", BadgeTagLine + AnnotationTool.Freehand),
                        ("圈注", BadgeTagLine + AnnotationTool.Circle), ("填充", BadgeTagLine + AnnotationTool.Fill)
                    });
                    AddBadgeRow("笔触", new[]
                    {
                        ("标准平滑", BadgeTagStyle + InkStrokeStyle.Standard),
                        ("手绘质感", BadgeTagStyle + InkStrokeStyle.HandDrawn)
                    });
                }
                break;
            case BadgeMenuKind.Style:
                AddBadgeRow("笔触", new[]
                {
                    ("标准平滑", BadgeTagStyle + InkStrokeStyle.Standard),
                    ("手绘质感", BadgeTagStyle + InkStrokeStyle.HandDrawn)
                });
                break;
            case BadgeMenuKind.Size:
                var eraser = _annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick;
                AddBadgeRow(eraser ? "擦头直径" : "线径", new[]
                {
                    ("1 px", BadgeTagSize + "1"), ("2 px", BadgeTagSize + "2"), ("4 px", BadgeTagSize + "4"),
                    ("7 px", BadgeTagSize + "7"), ("11 px", BadgeTagSize + "11"), ("18 px", BadgeTagSize + "18")
                });
                break;
            case BadgeMenuKind.Opacity:
                AddBadgeRow("透明度", new[]
                {
                    ("35%", BadgeTagOpacity + "0.35"), ("55%", BadgeTagOpacity + "0.55"),
                    ("80%", BadgeTagOpacity + "0.8"), ("100%", BadgeTagOpacity + "1"),
                    ("高亮", BadgeTagOpacity + "highlight")
                });
                break;
            case BadgeMenuKind.Color:
                AddBadgeRow("颜色", new[]
                {
                    ("红", BadgeTagColor + "240,72,72"), ("橙", BadgeTagColor + "244,140,52"),
                    ("黄", BadgeTagColor + "236,200,56"), ("绿", BadgeTagColor + "80,206,108"),
                    ("青", BadgeTagColor + "56,208,214"), ("蓝", BadgeTagColor + "64,150,246"),
                    ("紫", BadgeTagColor + "168,110,226"), ("白", BadgeTagColor + "244,246,250"),
                    ("调色板…", BadgeTagColor + "palette")
                });
                break;
        }
        ToolBadgeMenu.Visibility = Visibility.Visible;
    }

    private void CloseBadgeMenu()
    {
        ToolBadgeMenu.Visibility = Visibility.Collapsed;
        _badgeMenuKind = BadgeMenuKind.None;
    }

    private void AddBadgeInfo(string message)
    {
        ToolBadgeMenuItems.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeMutedTextBrush"),
            FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
            Margin = new Thickness(2, 4, 2, 6)
        });
    }

    private void AddBadgeRow(string title, (string Label, string Tag)[] choices)
    {
        var caption = new TextBlock
        {
            Text = title,
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeAccentBrush"),
            FontSize = 10,
            Margin = new Thickness(2, 6, 2, 4)
        };
        ToolBadgeMenuItems.Children.Add(caption);
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var (label, tag) in choices)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = label,
                Tag = tag,
                FontSize = 10,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                Foreground = (System.Windows.Media.Brush)FindResource("ThemeTextBrush"),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            button.Click += BadgeChoice_Click;
            panel.Children.Add(button);
        }
        ToolBadgeMenuItems.Children.Add(panel);
    }

    private void ApplyBadgeSize(double size)
    {
        if (_annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick)
        {
            _eraserSize = size;
            return;
        }
        _annotationSize = size;
        if (_selectedKind == ElementKind.Stroke && _selectedIndex >= 0 && _selectedIndex < _annotations.Count)
        {
            _annotations[_selectedIndex].Thickness = ScreenToImageThickness(size);
            RedrawAnnotations();
        }
    }

    private void BadgeChoice_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.Button)?.Tag as string ?? string.Empty;
        if (tag.StartsWith(BadgeTagLine, StringComparison.Ordinal) &&
            Enum.TryParse<AnnotationTool>(tag[BadgeTagLine.Length..], out var tool))
        {
            if (_selectedKind == ElementKind.Stroke) ChangeSelectedLineType(tool);
            else SetAnnotationTool(tool);
        }
        else if (tag.StartsWith(BadgeTagStyle, StringComparison.Ordinal) &&
                 Enum.TryParse<InkStrokeStyle>(tag[BadgeTagStyle.Length..], out var style))
            SetLineStyle(style);
        else if (tag.StartsWith(BadgeTagSize, StringComparison.Ordinal) &&
                 double.TryParse(tag[BadgeTagSize.Length..], out var size))
            ApplyBadgeSize(size);
        else if (tag.StartsWith(BadgeTagOpacity, StringComparison.Ordinal))
        {
            var value = tag[BadgeTagOpacity.Length..];
            if (value == "highlight") SetOpacity(_annotationOpacity, !_annotationHighlighter);
            else if (double.TryParse(value, out var opacity)) SetOpacity(opacity, false);
        }
        else if (tag.StartsWith(BadgeTagColor, StringComparison.Ordinal))
        {
            var value = tag[BadgeTagColor.Length..];
            if (value == "palette") OpenColorPalette();
            else
            {
                var parts = value.Split(',');
                if (parts.Length == 3 &&
                    byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
                {
                    _annotationColor = Color.FromRgb(r, g, b);
                    ApplyColorToSelection();
                }
            }
        }
        CloseBadgeMenu();
        ShowToolBadge();
    }

    private void ToolBadgeName_Click(object sender, RoutedEventArgs e) => ShowBadgeMenu(BadgeMenuKind.LineType);
    private void ToolBadgeSize_Click(object sender, RoutedEventArgs e) => ShowBadgeMenu(BadgeMenuKind.Size);
    private void ToolBadgeOpacity_Click(object sender, RoutedEventArgs e) => ShowBadgeMenu(BadgeMenuKind.Opacity);
    private void ToolBadgeColorDot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ShowBadgeMenu(BadgeMenuKind.Color);
        e.Handled = true;
    }

    // ---------------- Esc handling ----------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_regionAction != RegionAction.None)
            {
                _regionAction = RegionAction.None;
                _regionSelecting = false;
                ClearRegionVisual();
                RestoreCursor();
                e.Handled = true;
                return;
            }
            if (_annotationTool != AnnotationTool.Pointer || _selectedKind != ElementKind.None)
            {
                ExitTool();
                ShowToolBadge();
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.Delete && _selectedKind != ElementKind.None && _selectedIndex >= 0)
        {
            DeleteSelectedElement();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
