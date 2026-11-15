namespace Nova.UI;

public enum IslandState
{
    Compact,
    Expanded,
    Hidden
}

public static class IslandDimensions
{
    public const double CompactWidth = 210.0;
    public const double CompactHeight = 42.0;
    public const double CompactCornerRadius = 21.0;

    // Wide panoramic HUD mode matching the reference design
    public const double ExpandedWidth = 920.0;
    public const double ExpandedHeight = 118.0;           // Sleek HUD bar height
    public const double ExpandedWorkspaceHeight = 330.0;  // Full slides workspace height (Notes, Tasks, Tools)
    public const double ExpandedCornerRadius = 24.0;

    public const double CompactWindowWidth = 260.0;
    public const double CompactWindowHeight = 70.0;
    public const double ExpandedWindowWidth = 960.0;
    public const double ExpandedWindowHeight = 380.0;

    public const double TopMargin = 12.0;
    public const int AnimationDurationMs = 300;
}

