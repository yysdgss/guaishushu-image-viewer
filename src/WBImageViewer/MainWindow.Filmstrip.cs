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
        foreach (var item in _playlist)
        {
            if (item.Thumbnail != null) continue;
            if (string.Equals(System.IO.Path.GetExtension(item.Path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
            {
                item.Brush = new SolidColorBrush(Color.FromRgb(108, 182, 255));
                item.Name += " · 工程";
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
            }
            catch
            {
                // PDF, SVG, PSD and other document formats have no WIC decoder;
                // ask Windows for a real page thumbnail before showing a plain tile.
                item.Thumbnail = ShellPreviewService.TryLoad(item.Path, 256);
                item.Brush = new SolidColorBrush(Color.FromRgb(140, 140, 150));
            }
        }
        // Bind once to the live list; later refreshes only repaint existing containers.
        if (!ReferenceEquals(FilmstripList.ItemsSource, _playlist))
        {
            FilmstripList.ItemsSource = _playlist;
            _filmVisualStates.Clear();
        }
        else
            FilmstripList.Items.Refresh();
        UpdateFilmstripSelection();
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
            ? (PrevBubble, PrevBubbleScale, PrevBubbleShift, -4d)
            : (NextBubble, NextBubbleScale, NextBubbleShift, 4d);

    private void ApplyNavZoneVisual(Border zone, bool enabled, string tooltip)
    {
        zone.ToolTip = tooltip;
        zone.Cursor = enabled ? Cursors.Hand : Cursors.No;
        var (bubble, scale, shift, edge) = NavParts(zone);
        var opacity = !enabled ? 0.02 : zone.IsMouseOver ? 0.7 : 0.04;
        var scaleX = !enabled ? 0.3 : zone.IsMouseOver ? 1 : 0.3;
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
            enabled ? 0.7 : 0.16, enabled ? 1 : 0.5, 260, true);
    }

    private void NavZone_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Border zone) return;
        var enabled = ReferenceEquals(zone, PrevZone) ? _prevNavEnabled : _nextNavEnabled;
        var (bubble, scale, shift, edge) = NavParts(zone);
        AnimateNavBubble(bubble, scale, shift, edge,
            enabled ? 0.04 : 0.02, 0.3, 180, false);
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

        var slide = new DoubleAnimation(expand ? 0 : 150, TimeSpan.FromMilliseconds(expand ? 300 : 200))
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
        _filmFlatCenters.Clear();
        for (var i = 0; i < FilmstripList.Items.Count; i++)
        {
            if (FilmstripList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            var group = EnsureFilmTransforms(item);
            ((ScaleTransform)group.Children[0]).ScaleX = DockRestScale;
            ((ScaleTransform)group.Children[0]).ScaleY = DockRestScale;
            ((TranslateTransform)group.Children[1]).X = 0;
            ((TranslateTransform)group.Children[1]).Y = 0;
            System.Windows.Controls.Panel.SetZIndex(item, 0);

            // At rest the only transform is the known ScaleTransform about RenderTransformOrigin
            // (0.5,1). Inverting that analytically gives the true unscaled layout centre.
            var anchor = item.TransformToAncestor(FilmstripPanel)
                .Transform(new Point(0, item.ActualHeight));
            _filmFlatCenters[item] = anchor.X + item.ActualWidth * 0.5 * DockRestScale;
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

    // macOS Dock tuning constants.
    private const double DockRestScale = 0.85;   // icon size when the pointer is far away
    private const double DockPeakScale = 1.75;   // icon size directly under the pointer
    private const double DockSpread = 118;       // half-width of the influence region (px)
    private const double DockLift = 20;          // upward lift of the peak icon (px)
    private const double DockSpring = 0.24;      // spring stiffness toward the target
    private const double DockDamping = 0.74;     // velocity damping

    /// <summary>
    /// macOS Dock magnification, rebuilt from the patent's cosine falloff rather than a
    /// Gaussian. Each icon's scale is h + (H-h)*cos(pi*d / 2W) clamped at d = W, which reaches
    /// rest size with zero slope at the region edge (no hard cutoff). Neighbour displacement
    /// is derived from the difference of the scales on each side of an icon, so the row tiles
    /// exactly: magnified icons push neighbours aside with no overlap and no gaps.
    /// Values are integrated with a damped spring each frame for the characteristic bounce.
    /// </summary>
    private void FilmRendering(object? sender, EventArgs e)
    {
        if (!_filmstripExpanded) return;
        var items = new List<ListBoxItem>();
        for (var i = 0; i < FilmstripList.Items.Count; i++)
            if (FilmstripList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                items.Add(item);
        if (items.Count == 0) return;

        // Flat (untransformed) layout centres. Recomputing them from the live render transform
        // on every frame fed magnification back into its own input and caused jitter, so each
        // container's centre is captured once. Containers may be unrealized when the strip is
        // first expanded, so a missing entry is filled in here from the current known transform.
        var centers = new double[items.Count];
        var widths = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!_filmFlatCenters.TryGetValue(item, out var cx))
            {
                var state0 = _filmVisualStates.TryGetValue(item, out var s0)
                    ? s0.Scale : DockRestScale;
                var tx = _filmVisualStates.TryGetValue(item, out var s1) ? s1.OffsetX : 0;
                var anchor = item.TransformToAncestor(FilmstripPanel)
                    .Transform(new Point(0, item.ActualHeight));
                cx = anchor.X + item.ActualWidth * 0.5 * state0 - tx;
                _filmFlatCenters[item] = cx;
            }
            centers[i] = cx;
            widths[i] = Math.Max(1, item.ActualWidth + 12);
        }

        var pointerX = double.IsNaN(_filmPointerX) ? centers[items.Count / 2] : _filmPointerX;
        var active = !double.IsNaN(_filmPointerX);

        // 1. Spring-integrate each icon's scale toward the cosine target.
        var scales = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double weight = 0;
            if (active)
            {
                var d = Math.Abs(centers[i] - pointerX);
                if (d < DockSpread)
                    weight = Math.Cos(Math.PI * d / (2 * DockSpread));
            }
            var target = DockRestScale + (DockPeakScale - DockRestScale) * weight;

            if (!_filmVisualStates.TryGetValue(item, out var state))
            {
                state = new FilmVisualState { Scale = DockRestScale };
                _filmVisualStates[item] = state;
            }
            state.ScaleVel = (state.ScaleVel + (target - state.Scale) * DockSpring) * DockDamping;
            state.Scale += state.ScaleVel;
            scales[i] = state.Scale;
        }

        // 2. Exact tiling: shift each icon by half the extra width accumulated on either side.
        //    This is the discrete form of the patent's edge-sampled magnification function.
        var extras = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
            extras[i] = widths[i] * (scales[i] - DockRestScale);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double leftExtra = 0, rightExtra = 0;
            for (var j = 0; j < i; j++) leftExtra += extras[j];
            for (var j = i + 1; j < items.Count; j++) rightExtra += extras[j];

            // Keep the whole strip centred while icons slide apart around the pointer.
            var targetOffsetX = (leftExtra - rightExtra) * 0.5;
            var weight = (scales[i] - DockRestScale) / (DockPeakScale - DockRestScale);
            var targetOffsetY = -DockLift * weight;

            if (!_filmVisualStates.TryGetValue(item, out var state)) continue;
            state.OffsetX += (targetOffsetX - state.OffsetX) * DockSpring;
            state.OffsetY += (targetOffsetY - state.OffsetY) * DockSpring;

            var group = EnsureFilmTransforms(item);
            ((ScaleTransform)group.Children[0]).ScaleX = scales[i];
            ((ScaleTransform)group.Children[0]).ScaleY = scales[i];
            ((TranslateTransform)group.Children[1]).X = state.OffsetX;
            ((TranslateTransform)group.Children[1]).Y = state.OffsetY;
            System.Windows.Controls.Panel.SetZIndex(item, (int)(scales[i] * 100));
        }
    }

    /// <summary>
    /// Magnified thumbnails extend far beyond the ListBox viewport. WPF will not route a
    /// click on that overhang to the container, so resolve the nearest icon from the
    /// pointer position ourselves and open it explicitly.
    /// </summary>
    private void FilmstripHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_filmstripExpanded || FilmstripList.Items.Count == 0) return;
        var pointer = e.GetPosition(FilmstripPanel).X;
        var bestIndex = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < FilmstripList.Items.Count; i++)
        {
            if (FilmstripList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            if (!_filmFlatCenters.TryGetValue(item, out var cx))
            {
                var anchor = item.TransformToAncestor(FilmstripPanel)
                    .Transform(new Point(0, item.ActualHeight));
                cx = anchor.X + item.ActualWidth * 0.5 * DockRestScale;
            }
            // Include the current slide-aside offset so a magnified icon stays clickable.
            var center = cx + (_filmVisualStates.TryGetValue(item, out var state) ? state.OffsetX : 0);
            var distance = Math.Abs(pointer - center);
            if (distance < bestDistance) { bestDistance = distance; bestIndex = i; }
        }
        if (bestIndex < 0 || bestDistance > 90) return;

        _suppressFilmSelection = true;
        NavigateTo(bestIndex);
        FilmstripList.SelectedIndex = bestIndex;
        _suppressFilmSelection = false;
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
