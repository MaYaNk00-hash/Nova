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

    public const double ExpandedWidth = 420.0;
    public const double ExpandedHeight = 210.0;
    public const double ExpandedCornerRadius = 24.0;

    public const double TopMargin = 12.0;
    public const int AnimationDurationMs = 280;
}
