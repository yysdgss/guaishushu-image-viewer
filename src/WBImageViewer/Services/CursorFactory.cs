using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace WBImageViewer;

/// <summary>Built-in pen/eraser cursors created from embedded .cur resources with graceful fallback.</summary>
internal static class CursorFactory
{
    private static readonly Lazy<Cursor> PenLazy = new(() => Load("WBImageViewer.Resources.pen.cur", Cursors.Pen));
    private static readonly Lazy<Cursor> EraserLazy = new(() => Load("WBImageViewer.Resources.eraser.cur", Cursors.Cross));

    public static Cursor Pen => PenLazy.Value;
    public static Cursor Eraser => EraserLazy.Value;

    private static Cursor Load(string resource, Cursor fallback)
    {
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return stream != null ? new Cursor(stream) : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
