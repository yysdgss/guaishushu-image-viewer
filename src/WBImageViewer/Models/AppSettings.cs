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

    /// <summary>Panel background as #RRGGBB.</summary>
    public string BackgroundColor { get; set; } = "#101216";

    /// <summary>Panel opacity from 0.20 to 1.00.</summary>
    public double PanelOpacity { get; set; } = 1.0;

    /// <summary>Apply a conservative enhancement preset when an image is opened.</summary>
    public bool AutoOptimize { get; set; } = true;

    /// <summary>Last used adjustment values, so manual tuning survives a restart.</summary>
    public ImageAdjustments Adjustments { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowMoveMode MoveMode { get; set; } = WindowMoveMode.RightButton;

    public string ToggleTopMostHotkey { get; set; } = "Shift+Ctrl+Alt+T";
    public string FitWindowToImageHotkey { get; set; } = "Shift+Ctrl+Alt+R";
    public string FitImageToWindowHotkey { get; set; } = "Shift+Ctrl+Alt+S";

    /// <summary>Hold these modifiers while scrolling to zoom the window together with the image.</summary>
    public string WheelWindowModifiers { get; set; } = "Ctrl";

    public double ZoomStep { get; set; } = 1.2;

    /// <summary>Panel corner radius in device-independent pixels.</summary>
    public int CornerRadius { get; set; } = 4;

    public bool AssociateAsDefaultViewer { get; set; } = true;

    /// <summary>Original per-user default ProgIDs, kept so the association can be cleanly reverted.</summary>
    public Dictionary<string, string?> PreviousAssociations { get; set; } = new();
}
