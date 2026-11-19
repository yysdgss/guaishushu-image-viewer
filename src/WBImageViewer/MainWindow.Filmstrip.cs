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
                image.DecodePixelWidth = 120;
                image.UriSource = new Uri(item.Path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                item.Thumbnail = image;
            }
            catch
            {
                item.Brush = new SolidColorBrush(Color.FromRgb(140, 140, 150));
            }
        }
        // Bind once to the live list; later refreshes only repaint existing containers.
        if (!ReferenceEquals(FilmstripList.ItemsSource, _playlist))
            FilmstripList.ItemsSource = _playlist;
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
        if (_playlist.Count < 2) return;
        var index = CurrentPlaylistIndex();
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
        if (string.Equals(System.IO.Path.GetExtension(path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
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

    private static void ApplyNavZoneVisual(Border zone, bool enabled, string tooltip)
    {
        zone.ToolTip = tooltip;
        zone.Cursor = enabled ? Cursors.Hand : Cursors.No;
        zone.Opacity = enabled ? (zone.IsMouseOver ? 1 : 0.22) : 0.1;
    }

    private void FlashNavZone(Border zone)
    {
        var anim = new DoubleAnimation(0.14, 0.9, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            FillBehavior = FillBehavior.Stop
        };
        zone.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void NavZone_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Border zone) return;
        var enabled = ReferenceEquals(zone, PrevZone) ? _prevNavEnabled : _nextNavEnabled;
        zone.Opacity = enabled ? 1 : 0.2;
    }

    private void NavZone_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Border zone) return;
        var enabled = ReferenceEquals(zone, PrevZone) ? _prevNavEnabled : _nextNavEnabled;
        zone.Opacity = enabled ? 0.22 : 0.1;
    }

    // ---------------- bottom filmstrip island ----------------

    private void FilmstripBar_MouseEnter(object sender, MouseEventArgs e)
    {
        _filmstripLeaveTimer?.Stop();
        AnimateFilmstrip(true);
    }

    private void FilmstripBar_MouseLeave(object sender, MouseEventArgs e)
    {
        _filmstripLeaveTimer?.Stop();
        _filmstripLeaveTimer?.Start();
    }

    private void FilmstripHost_MouseLeave(object sender, MouseEventArgs e)
    {
        _filmstripLeaveTimer?.Stop();
        _filmstripLeaveTimer?.Start();
    }

    private void AnimateFilmstrip(bool expand)
    {
        if (_filmstripExpanded == expand) return;
        _filmstripExpanded = expand;
        FilmstripHost.IsHitTestVisible = true;

        var slide = new DoubleAnimation(expand ? 0 : 98, TimeSpan.FromMilliseconds(expand ? 260 : 200))
        {
            EasingFunction = new QuadraticEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        FilmstripSlide.BeginAnimation(TranslateTransform.YProperty, slide);

        var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 220 : 170))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        if (!expand)
            fade.Completed += (_, _) => { if (!_filmstripExpanded) FilmstripHost.IsHitTestVisible = false; };
        FilmstripHost.BeginAnimation(OpacityProperty, fade);

        if (expand) RefreshFilmstrip();
    }

    /// <summary>
    /// iOS cover-flow style: thumbnails near the pointer grow and lift toward the viewer,
    /// distant ones shrink. Weights are computed per frame from the pointer's X position.
    /// </summary>
    private void FilmstripHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_filmstripExpanded) return;
        var mouse = e.GetPosition(FilmstripPanel);
        const double sigma = 74;
        for (var i = 0; i < FilmstripList.Items.Count; i++)
        {
            if (FilmstripList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            var anchor = item.TransformToAncestor(FilmstripPanel).Transform(new Point(0, item.ActualHeight));
            var centerX = anchor.X + item.ActualWidth / 2;
            var dx = mouse.X - centerX;
            var weight = Math.Exp(-(dx * dx) / (2 * sigma * sigma));
            var scale = 0.8 + 0.5 * weight;
            var lift = -12 * weight;

            if (item.RenderTransform is not TransformGroup group ||
                group.Children.Count != 2 ||
                group.Children[0] is not ScaleTransform st ||
                group.Children[1] is not TranslateTransform tt)
            {
                st = new ScaleTransform();
                tt = new TranslateTransform();
                group = new TransformGroup();
                group.Children.Add(st);
                group.Children.Add(tt);
                item.RenderTransform = group;
            }
            st.ScaleX = st.ScaleY = scale;
            tt.Y = lift;
            Canvas.SetZIndex(item, (int)(weight * 100));
        }
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
