using System.Text.Json.Serialization;

namespace WBImageViewer.Models;

public enum WindowMoveMode
{
    RightButton,
    MiddleButton,
    SpaceAndLeftButton
}

public enum CompassModifier
{
    Ctrl,
    Shift,
    Alt,
    CtrlShift,
    CtrlAlt,
    ShiftAlt
}

public enum CompassButton
{
    Right,
    Middle
}

public enum ScreenshotMode
{
    FullScreen,
    Window,
    Region,
    Freeform
}

public sealed class AppSettings
{
    public bool SingleWindow { get; set; } = true;

    /// <summary>
    /// When true, next/previous wraps around at the end of the playlist. When false,
    /// the edge buttons are disabled at the first/last picture and flash on click.
    /// </summary>
    public bool LoopPlaylist { get; set; } = true;

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

    /// <summary>Modifier held while pressing CompassButton to open the annotation compass.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompassModifier CompassModifier { get; set; } = CompassModifier.Ctrl;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompassButton CompassButton { get; set; } = CompassButton.Right;

    public string ToggleTopMostHotkey { get; set; } = "Shift+Ctrl+Alt+T";
    public string FitWindowToImageHotkey { get; set; } = "Shift+Ctrl+Alt+R";
    public string FitImageToWindowHotkey { get; set; } = "Shift+Ctrl+Alt+S";
    public string ScreenshotHotkey { get; set; } = "Shift+Ctrl+Alt+X";

    /// <summary>Hold these modifiers while scrolling to zoom the window together with the image.</summary>
    public string WheelWindowModifiers { get; set; } = "Ctrl";

    public double ZoomStep { get; set; } = 1.2;

    /// <summary>Panel corner radius in device-independent pixels.</summary>
    public int CornerRadius { get; set; } = 4;

    public bool AssociateAsDefaultViewer { get; set; } = true;

    /// <summary>Original per-user default ProgIDs, kept so the association can be cleanly reverted.</summary>
    public Dictionary<string, string?> PreviousAssociations { get; set; } = new();

    /// <summary>Natural writing pen width used as the default brush diameter (px).</summary>
    public double DefaultPenWidth { get; set; } = 3;

    /// <summary>Default stroke opacity. 0.45 mimics a PPT highlighter mark.</summary>
    public double DefaultOpacity { get; set; } = 0.45;

    /// <summary>Whether new strokes start in translucent highlighter mode.</summary>
    public bool DefaultHighlighter { get; set; } = false;

    // ---- Multimodal AI (OpenAI-compatible chat/completions) ----
    public string AiEndpoint { get; set; } = "https://api.openai.com/v1";
    public string AiKey { get; set; } = "";
    public string AiModel { get; set; } = "gpt-4o-mini";
    public string AiTargetLanguage { get; set; } = "简体中文";
}
