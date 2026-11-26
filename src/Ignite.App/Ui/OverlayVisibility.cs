namespace Ignite.App.Ui;

/// <summary>Shows the stream controls after mouse activity and fades them out once the mouse rests.</summary>
internal sealed class OverlayVisibility
{
    public const double VisibleSeconds = 3;
    public const double FadeSeconds = 0.3;

    private double _lastActivity = double.NegativeInfinity;

    public void Activity(double now) => _lastActivity = now;

    /// <summary>Opacity at <paramref name="now"/>; pinned controls stay fully visible and restart the timer.</summary>
    public float Opacity(double now, bool pinned)
    {
        if (pinned)
            _lastActivity = now;
        var fading = now - _lastActivity - VisibleSeconds;
        return (float)Math.Clamp(1 - fading / FadeSeconds, 0, 1);
    }
}
