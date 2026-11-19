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

    private void ShowToolBadge()
    {
        var name = _annotationTool switch
        {
            AnnotationTool.Line => "直线",
            AnnotationTool.Wave => "波浪线",
            AnnotationTool.DoubleLine => "双线",
            AnnotationTool.Freehand => "自由画",
            AnnotationTool.Circle => "手绘圈注",
            AnnotationTool.Fill => "油漆桶填充",
            AnnotationTool.EraserFree => "自由擦除",
            AnnotationTool.EraserClick => "点击擦除",
            AnnotationTool.Move => "移动元素",
            _ => string.Empty
        };
        var show = !string.IsNullOrEmpty(name) || _selectedKind != ElementKind.None;
        if (!show)
        {
            ToolBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var diameter = _annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick
            ? _eraserSize : _annotationSize;
        ToolBadgeName.Content = _selectedKind != ElementKind.None ? $"已选中 · {name}" : $"标注：{name}";
        ToolBadgeSize.Content = $"直径 {diameter:0}";
        ToolBadgeOpacity.Content = _annotationHighlighter ? "高亮" : $"{_annotationOpacity:0%}";
        ToolBadgeColorDot.Fill = new SolidColorBrush(_annotationColor);
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
            _annotations.Add(new AnnotationStroke(_annotationTool, _draftPoints,
                _annotationColor, _annotationSize)
            {
                Opacity = _annotationOpacity,
                Highlighter = _annotationHighlighter
            });
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
                _annotations[_selectedIndex].HitTest(vp, ToViewportSpace, radius),
            ElementKind.Fill when _selectedIndex >= 0 && _selectedIndex < _fills.Count =>
                _fills[_selectedIndex].HitTest(vp, ToViewportSpace, radius),
            ElementKind.Text when _selectedIndex >= 0 && _selectedIndex < _textCards.Count =>
                _textCards[_selectedIndex].HitTest(vp, ToViewportSpace, ImageView.ActualWidth, ImageView.ActualHeight, radius),
            _ => false
        };
    }

    private bool TryBeginElementEdit(Point vp)
    {
        var handleRadius = 9;
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
                    _editStartImage = vp;
                    _editOriginalPoints = stroke.Points.ToList();
                    var bounds = stroke.GetHandles();
                    _editOriginalTopLeft = bounds.Count > 0 ? bounds[0].Value : new Point(0, 0);
                    _editOriginalBottomRight = bounds.Count > 1 ? bounds[1].Value : new Point(1, 1);
                    CaptureMouse();
                    return true;
                }
            }
            if (stroke.HitTest(vp, ToViewportSpace, Math.Max(10, stroke.Thickness)))
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
            if (_annotations[i].HitTest(vp, ToViewportSpace, Math.Max(12, _annotationSize * 2)))
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
                    stroke.ApplyHandle(_editingHandle,
                        new Point(_editOriginalPoints![_editingHandle].X + dx, _editOriginalPoints[_editingHandle].Y + dy),
                        _editOriginalTopLeft, _editOriginalBottomRight);
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
                var copy = new AnnotationStroke(s.Tool, s.Points.Select(p => new Point(p.X + offset.X, p.Y + offset.Y)),
                    s.Color, s.Thickness) { Opacity = s.Opacity, Highlighter = s.Highlighter };
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
        stroke.Thickness = Math.Clamp(stroke.Thickness + delta, 1, 24);
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

    private void RedrawAnnotations()
    {
        AnnotationLayer.Children.Clear();
        foreach (var fill in _fills)
        {
            var visual = fill.CreateVisual();
            if (visual == null) continue;
            var tl = ToViewportSpace(new Point(0, 0));
            Canvas.SetLeft(visual, tl.X);
            Canvas.SetTop(visual, tl.Y);
            ((System.Windows.Controls.Image)visual).Width = ImageView.ActualWidth;
            ((System.Windows.Controls.Image)visual).Height = ImageView.ActualHeight;
            AnnotationLayer.Children.Add(visual);
        }
        for (var i = 0; i < _annotations.Count; i++)
            AnnotationLayer.Children.Add(_annotations[i].CreateVisual(ToViewportSpace,
                _selectedKind == ElementKind.Stroke && _selectedIndex == i));
        for (var i = 0; i < _textCards.Count; i++)
            AnnotationLayer.Children.Add(_textCards[i].CreateVisual(ToViewportSpace, ImageView.ActualWidth,
                _selectedKind == ElementKind.Text && _selectedIndex == i));
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
            var draft = new AnnotationStroke(_annotationTool, _draftPoints, _annotationColor, _annotationSize)
            {
                Opacity = _annotationOpacity, Highlighter = _annotationHighlighter
            };
            _draftVisual = draft.CreateVisual(ToViewportSpace);
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
            if (_annotations[i].HitTest(viewportPoint, ToViewportSpace, radius))
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
        _regionAction = action;
        SetGrabCursor();
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
        var scale = _pixelWidth / Math.Max(1.0, ImageView.ActualWidth);
        var sx = Math.Clamp((int)(rect.Left * scale), 0, Math.Max(0, _pixelWidth - 1));
        var sy = Math.Clamp((int)(rect.Top * scale), 0, Math.Max(0, _pixelHeight - 1));
        var sw = Math.Clamp((int)(rect.Width * scale), 1, _pixelWidth - sx);
        var sh = Math.Clamp((int)(rect.Height * scale), 1, _pixelHeight - sy);
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
        return true;
    }

    // ---------------- screenshot ----------------

    private void StartScreenshot()
    {
        var shot = new Views.ScreenshotWindow { Owner = this };
        if (shot.ShowDialog() == true && shot.CapturedImage != null)
            App.OpenBitmap(shot.CapturedImage, "屏幕截图");
    }

    // ---------------- badge quick controls ----------------

    private void ToolBadgeName_Click(object sender, RoutedEventArgs e)
    {
        var order = new[]
        {
            AnnotationTool.Line, AnnotationTool.Wave, AnnotationTool.DoubleLine,
            AnnotationTool.Freehand, AnnotationTool.Circle
        };
        var idx = Array.IndexOf(order, _annotationTool);
        var next = order[(idx + 1) % order.Length];
        SetAnnotationTool(next);
        ShowToolBadge();
    }

    private void ToolBadgeSize_Click(object sender, RoutedEventArgs e)
    {
        var sizes = new double[] { 1, 2, 3, 5, 8, 12 };
        if (_annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick)
        {
            var idx = Array.IndexOf(sizes, _eraserSize);
            _eraserSize = sizes[(idx + 1) % sizes.Length];
        }
        else
        {
            var idx = Array.IndexOf(sizes, _annotationSize);
            _annotationSize = idx < 0 ? 3 : sizes[(idx + 1) % sizes.Length];
        }
        ShowToolBadge();
    }

    private void ToolBadgeOpacity_Click(object sender, RoutedEventArgs e)
    {
        var steps = new double[] { 0.35, 0.55, 0.8, 1.0 };
        var idx = Array.FindIndex(steps, s => Math.Abs(s - _annotationOpacity) < 0.02);
        SetOpacity(steps[(idx + 1) % steps.Length], false);
        ShowToolBadge();
    }

    private void ToolBadgeColorDot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenColorPalette();
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
