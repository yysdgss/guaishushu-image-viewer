using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WBImageViewer.Services;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace WBImageViewer;

/// <summary>
/// Bottom "dynamic island" filmstrip plus the left/right arc page-turn zones. A batch of
/// dropped files stays as an isolated carousel; otherwise the current image's directory
/// is used. The 4px white bar is only the visible trigger - a taller transparent hit
/// area sits above the WindowChrome resize border so hover always reaches it.
/// </summary>
public partial class MainWindow
{
    public void SetPlaylist(IReadOnlyList<string> files, bool fromDrop)
    {
        _playlist.Clear();
        foreach (var file in files)
            if (ImageFile.IsSupported(file)) _playlist.Add(CreateFilmItem(file));
        _playlistFromDrop = fromDrop;
        RefreshFilmstrip();
    }

    private static FilmItem CreateFilmItem(string path) => new()
    {
        Path = path,
        Name = System.IO.Path.GetFileName(path)
    };

    internal void SyncPlaylistForCurrentImage()
    {
        if (string.IsNullOrEmpty(_currentImagePath)) return;
        if (_playlist.Any(p => string.Equals(p.Path, _currentImagePath, StringComparison.OrdinalIgnoreCase)))
        {
            // Same directory (or same dropped batch): only move the selection, never
            // rebuild thumbnails or rebind the list while paging.
            UpdateFilmstripSelection();
            return;
        }
        _playlistFromDrop = false;
        _playlist.Clear();
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_currentImagePath);
            if (!string.IsNullOrEmpty(dir))
                foreach (var file in Directory.EnumerateFiles(dir).Where(ImageFile.IsSupported).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    _playlist.Add(CreateFilmItem(file));
        }
        catch { /* unreadable directory: keep an empty strip */ }
        RefreshFilmstrip();
    }

    private void RefreshFilmstrip()
    {
        // Bind immediately so the strip layout appears without waiting for any decode.
        if (!ReferenceEquals(FilmstripList.ItemsSource, _playlist))
        {
            FilmstripList.ItemsSource = _playlist;
            _filmVisualStates.Clear();
        }
        else
            FilmstripList.Items.Refresh();
        UpdateFilmstripSelection();

        // Thumbnail decoding used to run synchronously for every file here — with a folder
        // of hundreds of photos that blocked the first paint for seconds. Schedule it as one
        // coalesced background pass; a newer refresh cancels the previous one's work.
        var generation = ++_thumbGeneration;
        Dispatcher.BeginInvoke(new Action(() => GenerateThumbnails(generation)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private int _thumbGeneration;

    private void GenerateThumbnails(int generation)
    {
        if (generation != _thumbGeneration) return;
        var changed = false;
        foreach (var item in _playlist)
        {
            if (generation != _thumbGeneration) return;
            if (item.Thumbnail != null) continue;
            if (string.Equals(System.IO.Path.GetExtension(item.Path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
            {
                item.Brush = new SolidColorBrush(Color.FromRgb(108, 182, 255));
                if (!item.Name.EndsWith(" · 工程")) item.Name += " · 工程";
                changed = true;
                continue;
            }
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 180;
                image.UriSource = new Uri(item.Path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                item.Thumbnail = image;
                changed = true;
            }
            catch
            {
                // PDF, SVG, PSD and other document formats have no WIC decoder;
                // ask Windows for a real page thumbnail before showing a plain tile.
                item.Thumbnail = ShellPreviewService.TryLoad(item.Path, 256);
                item.Brush = new SolidColorBrush(Color.FromRgb(140, 140, 150));
                changed = true;
            }
        }
        if (changed && generation == _thumbGeneration)
            FilmstripList.Items.Refresh();
    }

    private int CurrentPlaylistIndex() => _playlist.FindIndex(p =>
        string.Equals(p.Path, _currentImagePath, StringComparison.OrdinalIgnoreCase));

    private void UpdateFilmstripSelection()
    {
        _suppressFilmSelection = true;
        var index = CurrentPlaylistIndex();
        if (index >= 0 && FilmstripList.SelectedIndex != index)
            FilmstripList.SelectedIndex = index;
        FilmstripCounter.Text = _playlist.Count == 0 ? "0 / 0" : $"{index + 1} / {_playlist.Count}";
        if (index >= 0 && FilmstripList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            item.BringIntoView();
        _suppressFilmSelection = false;
        UpdateNavZoneState();
    }

    private void Navigate(int delta)
    {
        // A PDF behaves like a stack of pictures: arrows page through it first, then
        // cross over to the neighbouring file at the document boundary.
        var index = CurrentPlaylistIndex();
        if (PdfService.IsPdf(_currentImagePath) && _pdfPageCount > 1)
        {
            var page = _pdfPageIndex + delta;
            if (page >= 0 && page < _pdfPageCount)
            {
                _ = LoadPdfAsync(_currentImagePath, page);
                return;
            }
            if (App.Settings.LoopPlaylist)
            {
                _ = LoadPdfAsync(_currentImagePath, (page + _pdfPageCount) % _pdfPageCount);
                return;
            }
            // Fall through: cross into the previous/next file at the PDF boundary.
        }

        if (index < 0) index = 0;
        var target = index + delta;
        if (target < 0 || target >= _playlist.Count)
        {
            if (!App.Settings.LoopPlaylist)
            {
                FlashNavZone(delta < 0 ? PrevZone : NextZone);
                return;
            }
            target = (target + _playlist.Count) % _playlist.Count;
        }
        NavigateTo(target);
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        var path = _playlist[index].Path;
        if (PdfService.IsPdf(path))
            _ = LoadPdfAsync(path, 0);
        else if (string.Equals(Path.GetExtension(path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
            LoadProject(path);
        else
            LoadImage(path);
        UpdateFilmstripSelection();
    }

    private void PrevZone_Click(object sender, MouseButtonEventArgs e) { Navigate(-1); e.Handled = true; }
    private void NextZone_Click(object sender, MouseButtonEventArgs e) { Navigate(1); e.Handled = true; }

    private void UpdateNavZoneState()
    {
        var index = CurrentPlaylistIndex();
        var count = _playlist.Count;
        var loop = App.Settings.LoopPlaylist;
        _prevNavEnabled = count > 1 && (loop || index > 0);
        _nextNavEnabled = count > 1 && (loop || index >= 0 && index < count - 1);
        ApplyNavZoneVisual(PrevZone, _prevNavEnabled,
            !_prevNavEnabled ? "已到第一张（轮播已关闭）" : "上一张");
        ApplyNavZoneVisual(NextZone, _nextNavEnabled,
            !_nextNavEnabled ? "已到最后一张（轮播已关闭）" : "下一张");
    }

    private (FrameworkElement Bubble, ScaleTransform Scale, TranslateTransform Shift, double EdgeOffset) NavParts(Border zone)
        => ReferenceEquals(zone, PrevZone)
            ? (PrevBubble, PrevBubbleScale, PrevBubbleShift, -6d)
            : (NextBubble, NextBubbleScale, NextBubbleShift, 6d);

    private void ApplyNavZoneVisual(Border zone, bool enabled, string tooltip)
    {
        zone.ToolTip = tooltip;
        zone.Cursor = enabled ? Cursors.Hand : Cursors.No;
        var (bubble, scale, shift, edge) = NavParts(zone);
        var opacity = !enabled ? 0.02 : zone.IsMouseOver ? 0.75 : 0.05;
        var scaleX = !enabled ? 0.18 : zone.IsMouseOver ? 0.9 : 0.35;
        AnimateNavBubble(bubble, scale, shift, edge, opacity, scaleX, 190, false);
    }

    private static void AnimateNavBubble(FrameworkElement bubble, ScaleTransform scale, TranslateTransform shift,
        double edgeOffset, double opacity, double targetScaleX, int durationMs, bool grow)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        IEasingFunction ease = grow
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var fadeEase = new QuadraticEase { EasingMode = grow ? EasingMode.EaseOut : EasingMode.EaseIn };
        bubble.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(opacity, duration) { EasingFunction = fadeEase });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(targetScaleX, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(grow ? 0 : edgeOffset, duration) { EasingFunction = ease });
    }

    private void FlashNavZone(Border zone)
    {
        var (bubble, _, _, _) = NavParts(zone);
        var resting = zone.IsMouseOver ? 0.7 : 0.04;
        var pulse = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(resting, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
        bubble.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private void NavZone_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Border zone) return;
        var enabled = ReferenceEquals(zone, PrevZone) ? _prevNavEnabled : _nextNavEnabled;
        var (bubble, scale, shift, edge) = NavParts(zone);
        AnimateNavBubble(bubble, scale, shift, edge,
            enabled ? 0.75 : 0.18, enabled ? 0.9 : 0.5, 260, true);
    }

    private void NavZone_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Border zone) return;
        var enabled = ReferenceEquals(zone, PrevZone) ? _prevNavEnabled : _nextNavEnabled;
        var (bubble, scale, shift, edge) = NavParts(zone);
        AnimateNavBubble(bubble, scale, shift, edge,
            enabled ? 0.05 : 0.02, 0.35, 180, false);
    }

    // ---------------- bottom Dock filmstrip ----------------

    private void FilmstripBar_MouseEnter(object sender, MouseEventArgs e)
    {
        _filmstripLeaveTimer?.Stop();
        UpdateFilmPointer(e);
        AnimateFilmstrip(true);
    }

    private void FilmstripBar_MouseMove(object sender, MouseEventArgs e)
    {
        // The trigger strip stays above the expanded host, so its pointer position
        // must be forwarded or the magnified icon would freeze while sweeping across it.
        UpdateFilmPointer(e);
    }

    private void FilmstripBar_MouseLeave(object sender, MouseEventArgs e)
    {
        _filmstripLeaveTimer?.Stop();
        _filmstripLeaveTimer?.Start();
    }

    private void FilmstripHost_MouseLeave(object sender, MouseEventArgs e)
    {
        _filmPointerX = double.NaN;
        _filmstripLeaveTimer?.Stop();
        _filmstripLeaveTimer?.Start();
    }

    private void FilmstripHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_filmstripExpanded) return;
        UpdateFilmPointer(e);
    }

    private void UpdateFilmPointer(MouseEventArgs e)
    {
        // Coordinates outside the panel are still meaningful: they simply fall
        // beyond the row, which returns every thumbnail to its resting scale.
        _filmPointerX = e.GetPosition(FilmstripPanel).X;
    }

    private void AnimateFilmstrip(bool expand)
    {
        if (_filmstripExpanded == expand) return;
        _filmstripExpanded = expand;
        FilmstripHost.IsHitTestVisible = true;

        var slide = new DoubleAnimation(expand ? 0 : 250, TimeSpan.FromMilliseconds(expand ? 300 : 200))
        {
            EasingFunction = new CubicEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        FilmstripSlide.BeginAnimation(TranslateTransform.YProperty, slide);

        var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 220 : 170))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        if (!expand)
            fade.Completed += (_, _) =>
            {
                if (_filmstripExpanded) return;
                FilmstripHost.IsHitTestVisible = false;
                StopFilmRendering();
            };
        FilmstripHost.BeginAnimation(OpacityProperty, fade);

        if (expand)
        {
            RefreshFilmstrip();
            ResetFilmItemTransforms();
            StartFilmRendering();
        }
        else
            _filmPointerX = double.NaN;
    }

    private void StartFilmRendering()
    {
        if (_filmRenderingHooked) return;
        CompositionTarget.Rendering += FilmRendering;
        _filmRenderingHooked = true;
    }

    private void StopFilmRendering()
    {
        if (!_filmRenderingHooked) return;
        CompositionTarget.Rendering -= FilmRendering;
        _filmRenderingHooked = false;
    }

    private void ResetFilmItemTransforms()
    {
        _filmVisualStates.Clear();
        for (var i = 0; i < FilmstripList.Items.Count; i++)
        {
            if (FilmstripList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            var group = EnsureFilmTransforms(item);
            ((ScaleTransform)group.Children[0]).ScaleX = DockRestScale;
            ((ScaleTransform)group.Children[0]).ScaleY = DockRestScale;
            ((TranslateTransform)group.Children[1]).X = 0;
            ((TranslateTransform)group.Children[1]).Y = 0;
            System.Windows.Controls.Panel.SetZIndex(item, 0);
        }
    }

    private static TransformGroup EnsureFilmTransforms(ListBoxItem item)
    {
        if (item.RenderTransform is TransformGroup group &&
            group.Children.Count == 2 &&
            group.Children[0] is ScaleTransform &&
            group.Children[1] is TranslateTransform)
            return group;

        group = new TransformGroup();
        group.Children.Add(new ScaleTransform(DockRestScale, DockRestScale));
        group.Children.Add(new TranslateTransform());
        item.RenderTransform = group;
        return group;
    }

    // macOS Dock effect, rebuilt after the Tencent Cloud article "丝滑的 macOS Dock效果".
    // The pointer picks exactly ONE icon; only that icon and its two immediate
    // neighbours grow. The cursor's position INSIDE the hovered icon (offset 0..1)
    // splits the growth between the two neighbours, so sweeping across one icon
    // hands the peak continuously to the next - the article's key trick. Every
    // other icon stays at rest size. WPF has no flex layout, so neighbours are
    // displaced by the cumulative extra width, and a per-frame lerp plays the
    // role of the article's CSS `transition`.
    private const double DockRestScale = 0.85;    // --scale: 1 baseline (48px tiles rendered at 85%)
    private const double DockPeakScale = 1.60;    // hovered icon reaches this (article: 1.6x)
    private const double DockLiftPerScale = 22;   // upward lift per unit of extra scale (px)
    private const double DockFollow = 0.32;       // silky follow, the CSS transition equivalent
    private const double DockPickPadding = 8;     // forgiving hit area around each icon

    private List<ListBoxItem> RealizedFilmItems()
    {
        var items = new List<ListBoxItem>();
        for (var i = 0; i < FilmstripList.Items.Count; i++)
            if (FilmstripList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item &&
                item.ActualWidth > 0)
                items.Add(item);
        return items;
    }

    private Rect FilmBounds(ListBoxItem item)
        => item.TransformToAncestor(FilmstripPanel)
            .TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));

    /// <summary>
    /// Stable, unscaled centre of an item inside FilmstripPanel. RenderTransform does not
    /// affect layout, so subtracting our own slide offset from the visual centre recovers
    /// the fixed slot centre - immune to the magnification feedback loop.
    /// </summary>
    private double FilmLayoutCenter(ListBoxItem item)
    {
        var shiftX = _filmVisualStates.TryGetValue(item, out var s) ? s.OffsetX : 0;
        var bounds = FilmBounds(item);
        return (bounds.Left + bounds.Right) * 0.5 - shiftX;
    }

    /// <summary>
    /// Resolve the icon under the pointer. Visual bounds are tried first so a magnified
    /// overhang stays clickable; when the pointer sits in the gap BETWEEN two thumbnails
    /// no bound contains it, so we fall back to the nearest stable layout centre. That
    /// Voronoi-style assignment leaves no dead zone - the Dock never "drops" while the
    /// cursor sweeps across the row.
    /// </summary>
    private int HoveredFilmIndex(IReadOnlyList<ListBoxItem> items, double pointerX)
    {
        if (double.IsNaN(pointerX) || items.Count == 0) return -1;

        var best = -1;
        var bestDistance = double.MaxValue;
        var bestHalfWidth = 0d;
        for (var i = 0; i < items.Count; i++)
        {
            var bounds = FilmBounds(items[i]);
            var center = (bounds.Left + bounds.Right) * 0.5;
            var distance = Math.Abs(pointerX - center);
            if (distance >= bestDistance) continue;
            best = i;
            bestDistance = distance;
            bestHalfWidth = bounds.Width * 0.5;
        }
        if (bestDistance <= bestHalfWidth + DockPickPadding) return best;

        // Gap: assign to the nearest layout centre, which does not move with scale.
        var nearest = -1;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < items.Count; i++)
        {
            var distance = Math.Abs(pointerX - FilmLayoutCenter(items[i]));
            if (distance >= nearestDistance) continue;
            nearest = i;
            nearestDistance = distance;
        }
        var half = items[nearest].ActualWidth * 0.5;
        var first = FilmLayoutCenter(items[0]);
        var last = FilmLayoutCenter(items[items.Count - 1]);
        return pointerX >= first - half - DockPickPadding &&
               pointerX <= last + half + DockPickPadding ? nearest : -1;
    }

    /// <summary>
    /// Per-frame implementation of the article's Dock algorithm. With offset = the
    /// cursor's normalised position inside the hovered icon (0 at its left edge, 1 at
    /// its right edge, via (pointer - itemRect.left) / itemRect.width):
    ///   hovered  -> peak                          (always maximum)
    ///   left     -> rest + boost * (1 - offset)   (shrinks as the cursor moves right)
    ///   right    -> rest + boost * offset         (grows as the cursor moves right)
    ///   others   -> rest
    /// This is exactly the article's setProperty logic. Only three icons ever grow,
    /// and the peak slides from the left neighbour to the right one with no jump.
    /// </summary>
    private void FilmRendering(object? sender, EventArgs e)
    {
        if (!_filmstripExpanded) return;
        var items = RealizedFilmItems();
        if (items.Count == 0) return;

        var hovered = HoveredFilmIndex(items, _filmPointerX);

        // (pointerX - itemRect.left) / itemRect.width. Use the unscaled layout slot
        // (RenderTransform does not affect layout) so the value cannot feed its own
        // magnification back into the calculation and cause jitter.
        var offset = 0.5;
        if (hovered >= 0)
        {
            var hoveredItem = items[hovered];
            var layoutCenter = FilmLayoutCenter(hoveredItem);
            var left = layoutCenter - hoveredItem.ActualWidth * 0.5;
            offset = Math.Clamp((_filmPointerX - left) / Math.Max(1, hoveredItem.ActualWidth), 0, 1);
        }

        var boost = DockPeakScale - DockRestScale;
        var states = new FilmVisualState[items.Count];
        var scales = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!_filmVisualStates.TryGetValue(item, out var state))
            {
                state = new FilmVisualState { Scale = DockRestScale };
                _filmVisualStates[item] = state;
            }
            states[i] = state;

            double target;
            if (hovered < 0) target = DockRestScale;
            else if (i == hovered) target = DockPeakScale;
            else if (i == hovered - 1) target = DockRestScale + boost * (1 - offset);
            else if (i == hovered + 1) target = DockRestScale + boost * offset;
            else target = DockRestScale;

            // CSS `transition` equivalent: smooth ease toward the target, no spring
            // overshoot, so neighbouring icons never wobble after the pointer stops.
            state.Scale += (target - state.Scale) * DockFollow;
            scales[i] = state.Scale;
        }

        // A centred flex row pushes siblings aside by half of each icon's extra
        // width. Reproduce that with cumulative sums so grown icons tile exactly:
        // no overlap and no gaps, whatever the number of thumbnails.
        var extras = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
            extras[i] = items[i].ActualWidth * (scales[i] - DockRestScale);

        var rightSums = new double[items.Count];
        var tail = 0d;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            rightSums[i] = tail;
            tail += extras[i];
        }

        var leftSum = 0d;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var state = states[i];
            var targetOffsetX = (leftSum - rightSums[i]) * 0.5;
            leftSum += extras[i];

            var targetOffsetY = -(scales[i] - DockRestScale) * DockLiftPerScale;
            state.OffsetX += (targetOffsetX - state.OffsetX) * DockFollow;
            state.OffsetY += (targetOffsetY - state.OffsetY) * DockFollow;

            var group = EnsureFilmTransforms(item);
            ((ScaleTransform)group.Children[0]).ScaleX = scales[i];
            ((ScaleTransform)group.Children[0]).ScaleY = scales[i];
            ((TranslateTransform)group.Children[1]).X = state.OffsetX;
            ((TranslateTransform)group.Children[1]).Y = state.OffsetY;
            var distance = hovered < 0 ? int.MaxValue : Math.Abs(i - hovered);
            System.Windows.Controls.Panel.SetZIndex(item,
                distance == 0 ? 1000 : distance == 1 ? 500 : 0);
        }
    }

    /// <summary>
    /// Clicking anywhere on a thumbnail - including the enlarged overhang - opens that
    /// exact picture. The hovered index is derived from the same visual bounds used by
    /// the magnification loop, so the click target always matches what is under the mouse.
    /// </summary>
    private void FilmstripHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_filmstripExpanded) return;
        var pointer = e.GetPosition(FilmstripPanel).X;
        var items = RealizedFilmItems();
        var index = HoveredFilmIndex(items, pointer);
        if (index < 0) return;

        NavigateTo(index);
        e.Handled = true;
    }

    private void FilmstripList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilmSelection || FilmstripList.SelectedIndex < 0) return;
        NavigateTo(FilmstripList.SelectedIndex);
    }

    private void FilmstripList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = FindVisualChild<ScrollViewer>(FilmstripList);
        if (viewer != null)
        {
            viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
