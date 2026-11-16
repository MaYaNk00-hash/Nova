namespace Nova.UI;

public enum IslandState
{
    Circle,
    Compact,
    Expanded,
    Hidden
}

public enum PillTransparencyMode
{
    TranslucentGlass,   // Sleek subtle dark glass (~18-20% alpha) with luminous glass rim (Default)
    UltraClear,         // Completely transparent / crystal clear (~4% alpha or transparent)
    FrostedGlass,       // Rich frosted glass (~40% alpha)
    SolidDark           // Opaque obsidian dark (classic)
}

public static class IslandDimensions
{
    // Single Small Circle (Bubble Mode)
    public const double CircleWidth = 48.0;
    public const double CircleHeight = 48.0;
    public const double CircleCornerRadius = 24.0;
    public const double CircleWindowWidth = 110.0;
    public const double CircleWindowHeight = 96.0;

    // Compact Pill Notch (Minimalist: Pure Robot Face & Eyes)
    public const double CompactWidth = 156.0;              // Balanced minimalist notch for Robot Mascot
    public const double CompactHeight = 46.0;              // Sleek ergonomic height for big robo face
    public const double CompactCornerRadius = 23.0;

    // Compact Notch with Active Media (Dynamic height & width increase showcasing track below robot face)
    public const double CompactMediaWidth = 280.0;         // Extended width to comfortably display track title & controls
    public const double CompactMediaHeight = 78.0;         // Increased height: robot face on top, media track row below
    public const double CompactMediaCornerRadius = 22.0;
    public const double CompactMediaWindowWidth = 320.0;
    public const double CompactMediaWindowHeight = 98.0;

    // Wide panoramic HUD mode (increased height by 15% for crystal-clear graphics)
    public const double ExpandedWidth = 920.0;
    public const double ExpandedHeight = 126.0;           // Sleek HUD bar height (+15%)
    public const double ExpandedWorkspaceHeight = 380.0;  // Full slides workspace height (+15%)
    public const double ExpandedCornerRadius = 24.0;

    public const double CompactWindowWidth = 190.0;
    public const double CompactWindowHeight = 70.0;
    public const double ExpandedWindowWidth = 960.0;
    public const double ExpandedHudWindowHeight = 152.0;   // Height in HUD mode (no click blocking below bar)
    public const double ExpandedWindowHeight = 420.0;      // Height when Workspace drawer is open

    public const double TopMargin = 0.0;
    public const int AnimationDurationMs = 280;
}
