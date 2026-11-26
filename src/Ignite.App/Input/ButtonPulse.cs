using Ignite.Protocol.Input;

namespace Ignite.App.Input;

/// <summary>Holds a controller button down for a moment, for buttons pressed with the mouse.</summary>
internal sealed class ButtonPulse(ControllerButtons button, double seconds)
{
    private double _releaseAt = double.NegativeInfinity;

    public void Press(double now) => _releaseAt = now + seconds;

    public ControllerState Apply(ControllerState state, double now) =>
        now < _releaseAt ? state with { Buttons = state.Buttons | button } : state;
}
