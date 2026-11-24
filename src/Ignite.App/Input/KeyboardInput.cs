using Hexa.NET.SDL3;
using Ignite.Protocol.Input;

namespace Ignite.App.Input;

/// <summary>Keyboard fallback for playing without a gamepad.</summary>
internal static unsafe class KeyboardInput
{
    private static readonly (SDLScancode Key, ControllerButtons Button)[] Buttons =
    [
        (SDLScancode.Return, ControllerButtons.Cross),
        (SDLScancode.Backspace, ControllerButtons.Circle),
        (SDLScancode.Space, ControllerButtons.Square),
        (SDLScancode.Tab, ControllerButtons.Triangle),
        (SDLScancode.Up, ControllerButtons.DpadUp),
        (SDLScancode.Down, ControllerButtons.DpadDown),
        (SDLScancode.Left, ControllerButtons.DpadLeft),
        (SDLScancode.Right, ControllerButtons.DpadRight),
        (SDLScancode.Q, ControllerButtons.L1),
        (SDLScancode.E, ControllerButtons.R1),
        (SDLScancode.O, ControllerButtons.Options),
        (SDLScancode.P, ControllerButtons.PS),
        (SDLScancode.T, ControllerButtons.Touchpad),
    ];

    public static ControllerState Read()
    {
        int count;
        var state = SDL.GetKeyboardState(&count);
        var keys = new ReadOnlySpan<bool>(state, count).ToArray();
        bool Down(SDLScancode key) => (int)key < keys.Length && keys[(int)key];

        var buttons = ControllerButtons.None;
        foreach (var (key, button) in Buttons)
        {
            if (Down(key))
                buttons |= button;
        }

        return ControllerState.Idle with
        {
            Buttons = buttons,
            L2 = Down(SDLScancode.Scancode1) ? (byte)255 : (byte)0,
            R2 = Down(SDLScancode.Scancode3) ? (byte)255 : (byte)0,
            LeftX = Axis(Down(SDLScancode.A), Down(SDLScancode.D)),
            LeftY = Axis(Down(SDLScancode.W), Down(SDLScancode.S)),
            RightX = Axis(Down(SDLScancode.J), Down(SDLScancode.L)),
            RightY = Axis(Down(SDLScancode.I), Down(SDLScancode.K)),
        };
    }

    private static short Axis(bool negative, bool positive) =>
        negative == positive ? (short)0 : negative ? short.MinValue : short.MaxValue;
}
