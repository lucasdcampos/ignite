using Ignite.App.Input;
using Ignite.App.Streaming;
using Ignite.App.Ui;
using Ignite.Protocol;
using Ignite.Protocol.Input;

namespace Ignite.App.Tests;

public sealed class StreamControlsTests
{
    [Fact]
    public void ControlsStayVisibleForAFewSecondsAfterTheMouseMovesThenFadeOut()
    {
        var visibility = new OverlayVisibility();
        Assert.Equal(0, visibility.Opacity(10, pinned: false));

        visibility.Activity(10);

        Assert.Equal(1, visibility.Opacity(10, pinned: false));
        Assert.Equal(1, visibility.Opacity(10 + OverlayVisibility.VisibleSeconds, pinned: false));
        Assert.Equal(0.5f, visibility.Opacity(10 + OverlayVisibility.VisibleSeconds + OverlayVisibility.FadeSeconds / 2, pinned: false), 3);
        Assert.Equal(0, visibility.Opacity(10 + OverlayVisibility.VisibleSeconds + OverlayVisibility.FadeSeconds, pinned: false));
    }

    [Fact]
    public void PinnedControlsStayVisibleAndRestartTheTimerWhenReleased()
    {
        var visibility = new OverlayVisibility();
        visibility.Activity(0);

        Assert.Equal(1, visibility.Opacity(60, pinned: true));
        Assert.Equal(1, visibility.Opacity(60 + OverlayVisibility.VisibleSeconds, pinned: false));
        Assert.Equal(0, visibility.Opacity(61 + OverlayVisibility.VisibleSeconds, pinned: false));
    }

    [Fact]
    public void PulseHoldsTheButtonOnTopOfTheControllerStateUntilItExpires()
    {
        var pulse = new ButtonPulse(ControllerButtons.PS, 0.15);
        var state = ControllerState.Idle with { Buttons = ControllerButtons.Cross, LeftX = 100 };
        Assert.Equal(state, pulse.Apply(state, 1));

        pulse.Press(1);

        Assert.Equal(state with { Buttons = ControllerButtons.Cross | ControllerButtons.PS }, pulse.Apply(state, 1.1));
        Assert.Equal(state, pulse.Apply(state, 1.15));
    }

    [Fact]
    public async Task ConnectionIsRetriedWhileTheConsoleIsInUse()
    {
        var attempts = 0;
        var waits = 0;

        var result = await ConnectRetry.WhileInUseAsync(
            _ => ++attempts < 3 ? throw InUse() : Task.FromResult("session"),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1),
            () => waits++,
            CancellationToken.None);

        Assert.Equal("session", result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, waits);
    }

    [Fact]
    public async Task InUseIsReportedOnceTheTimeoutIsReached()
    {
        var attempts = 0;

        var error = await Assert.ThrowsAsync<RemotePlayException>(() => ConnectRetry.WhileInUseAsync<string>(
            _ =>
            {
                attempts++;
                throw InUse();
            },
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(20),
            () => { },
            CancellationToken.None));

        Assert.Equal(RemotePlayError.InUse, error.Error);
        Assert.InRange(attempts, 2, 6);
    }

    [Fact]
    public async Task OtherErrorsAreNotRetried()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<RemotePlayException>(() => ConnectRetry.WhileInUseAsync<string>(
            _ =>
            {
                attempts++;
                throw new RemotePlayException(RemotePlayError.Crashed, "crashed");
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1),
            () => { },
            CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CancellingStopsTheWait()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectRetry.WhileInUseAsync<string>(
            _ => throw InUse(),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(1),
            cancellation.Cancel,
            cancellation.Token));
    }

    private static RemotePlayException InUse() => RemotePlayException.FromApplicationReason(0x80108b10, "Session request");
}
