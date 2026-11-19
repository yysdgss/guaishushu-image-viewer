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

namespace WBImageViewer;

/// <summary>
/// Bottom "dynamic island" filmstrip plus left/right page-turn zones. A batch of dropped
/// files stays as an isolated carousel; otherwise the current image's directory is used.
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
        if (_playlistFromDrop && _playlist.Any(p => string.Equals(p.Path, _currentImagePath, StringComparison.OrdinalIgnoreCase)))
        {
            RefreshFilmstrip();
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
        _suppressFilmSelection = true;
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
        FilmstripList.ItemsSource = _playlist.ToList();
        var index = CurrentPlaylistIndex();
        if (index >= 0) FilmstripList.SelectedIndex = index;
        FilmstripCounter.Text = _playlist.Count == 0 ? "0 / 0" : $"{index + 1} / {_playlist.Count}";
        _suppressFilmSelection = false;
    }

    private int CurrentPlaylistIndex() => _playlist.FindIndex(p =>
        string.Equals(p.Path, _currentImagePath, StringComparison.OrdinalIgnoreCase));

    private void Navigate(int delta)
    {
        if (_playlist.Count < 2) return;
        var index = CurrentPlaylistIndex();
        if (index < 0) index = 0;
        NavigateTo((index + delta + _playlist.Count) % _playlist.Count);
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        var path = _playlist[index].Path;
        if (string.Equals(System.IO.Path.GetExtension(path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
            LoadProject(path);
        else
            LoadImage(path);
        RefreshFilmstrip();
    }

    private void PrevZone_Click(object sender, MouseButtonEventArgs e) { Navigate(-1); e.Handled = true; }
    private void NextZone_Click(object sender, MouseButtonEventArgs e) { Navigate(1); e.Handled = true; }

    private void NavZone_Enter(object sender, MouseEventArgs e)
    {
        if (sender is Border zone)
        {
            zone.Opacity = 1;
            zone.Background = new SolidColorBrush(Color.FromArgb(90, 53, 53, 55));
        }
    }

    private void NavZone_Leave(object sender, MouseEventArgs e)
    {
        if (sender is Border zone)
        {
            zone.Opacity = 0.25;
            zone.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void FilmstripBar_MouseEnter(object sender, MouseEventArgs e) => AnimateFilmstrip(true);
    private void FilmstripBar_MouseLeave(object sender, MouseEventArgs e) { }
    private void FilmstripHost_MouseLeave(object sender, MouseEventArgs e) => AnimateFilmstrip(false);

    private void AnimateFilmstrip(bool expand)
    {
        FilmstripHost.IsHitTestVisible = true;
        var slide = new DoubleAnimation(expand ? 0 : 98, TimeSpan.FromMilliseconds(expand ? 240 : 180))
        {
            EasingFunction = new QuadraticEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        FilmstripSlide.BeginAnimation(TranslateTransform.YProperty, slide);
        var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 200 : 140));
        FilmstripHost.BeginAnimation(OpacityProperty, fade);
        if (!expand) FilmstripHost.IsHitTestVisible = false;
        if (expand) RefreshFilmstrip();
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
