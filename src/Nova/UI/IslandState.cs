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
    public const double CompactHeight = 48.0;              // Increased by 15% for roomier, clearer layout
    public const double CompactCornerRadius = 24.0;

    // Wide panoramic HUD mode (increased height by 15% for crystal-clear graphics)
    public const double ExpandedWidth = 920.0;
    public const double ExpandedHeight = 126.0;           // Sleek HUD bar height (+15%)
    public const double ExpandedWorkspaceHeight = 380.0;  // Full slides workspace height (+15%)
    public const double ExpandedCornerRadius = 24.0;

    public const double CompactWindowWidth = 260.0;
    public const double CompactWindowHeight = 84.0;
    public const double ExpandedWindowWidth = 960.0;
    public const double ExpandedWindowHeight = 440.0;

    public const double TopMargin = 0.0;
    public const int AnimationDurationMs = 300;
}

