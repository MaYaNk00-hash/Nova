using System.Windows;
using System.Windows.Media.Animation;

namespace Nova.UI;

public class CornerRadiusAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(CornerRadius?), typeof(CornerRadiusAnimation), new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(CornerRadius?), typeof(CornerRadiusAnimation), new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(CornerRadiusAnimation), new PropertyMetadata(null));

    public CornerRadius? From
    {
        get => (CornerRadius?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public CornerRadius? To
    {
        get => (CornerRadius?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public CornerRadiusAnimation() { }

    public CornerRadiusAnimation(CornerRadius from, CornerRadius to, Duration duration)
    {
        From = from;
        To = to;
        Duration = duration;
    }

    public override Type TargetPropertyType => typeof(CornerRadius);

    protected override Freezable CreateInstanceCore() => new CornerRadiusAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        if (animationClock.CurrentProgress == null) return defaultDestinationValue;

        double progress = animationClock.CurrentProgress.Value;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? (CornerRadius)defaultOriginValue;
        var to = To ?? (CornerRadius)defaultDestinationValue;

        double topLeft = from.TopLeft + (to.TopLeft - from.TopLeft) * progress;
        double topRight = from.TopRight + (to.TopRight - from.TopRight) * progress;
        double bottomRight = from.BottomRight + (to.BottomRight - from.BottomRight) * progress;
        double bottomLeft = from.BottomLeft + (to.BottomLeft - from.BottomLeft) * progress;

        return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
    }
}

