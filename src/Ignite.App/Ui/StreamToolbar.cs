using System.Numerics;
using Hexa.NET.ImGui;

namespace Ignite.App.Ui;

internal enum ToolbarAction
{
    None,
    Fullscreen,
    PsButton,
    Settings,
}

/// <summary>
/// The stream controls along the bottom edge, over a translucent black bar as wide as the
/// window: settings on the left, the PS button (only when playing) in the middle and
/// fullscreen on the right.
/// </summary>
internal static class StreamToolbar
{
    private const float ButtonSize = 44;
    private const float Margin = 24;
    private const float BarOpacity = 0.5f;

    /// <summary>Height of the bar along the bottom of the window, in design units.</summary>
    public const float Clearance = ButtonSize + Margin * 2;

    /// <summary>Distance from the window's side edges to the corner buttons, in design units.</summary>
    public const float SideMargin = Margin;

    /// <summary>
    /// Draws the buttons. They never take keyboard or gamepad focus, so clicking them leaves the
    /// controller with the game.
    /// </summary>
    public static ToolbarAction Draw(bool fullscreen, bool playing, float opacity, out bool hovered)
    {
        var viewport = ImGui.GetMainViewport();
        var bottom = viewport.WorkPos.Y + viewport.WorkSize.Y - Theme.Dp(Margin);
        var left = viewport.WorkPos.X + Theme.Dp(Margin);
        var right = viewport.WorkPos.X + viewport.WorkSize.X - Theme.Dp(Margin);
        var middle = viewport.WorkPos.X + viewport.WorkSize.X / 2;

        var action = ToolbarAction.None;
        hovered = false;
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, opacity);
        DrawBar(viewport);
        if (Place("settings", new Vector2(left, bottom), new Vector2(0, 1), () => RoundButton(Icons.Settings, "Stream settings (F1)"), ref hovered))
            action = ToolbarAction.Settings;
        if (playing && Place("ps", new Vector2(middle, bottom), new Vector2(0.5f, 1), PsButton, ref hovered))
            action = ToolbarAction.PsButton;
        var fullscreenTooltip = fullscreen ? "Exit fullscreen (F11)" : "Fullscreen (F11)";
        if (Place("fullscreen", new Vector2(right, bottom), new Vector2(1, 1), () => RoundButton(fullscreen ? Icons.Minimize : Icons.Maximize, fullscreenTooltip), ref hovered))
            action = ToolbarAction.Fullscreen;
        ImGui.PopStyleVar();
        return action;
    }

    /// <summary>Drawn behind every window but over the video, so the buttons sit on top of it.</summary>
    private static void DrawBar(ImGuiViewportPtr viewport)
    {
        var max = viewport.WorkPos + viewport.WorkSize;
        var min = new Vector2(viewport.WorkPos.X, max.Y - Theme.Dp(Clearance));
        ImGui.GetBackgroundDrawList(viewport).AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0, 0, 0, BarOpacity)));
    }

    /// <summary>Each button gets its own small window, so the space between them stays free.</summary>
    private static bool Place(string id, Vector2 position, Vector2 pivot, Func<bool> button, ref bool hovered)
    {
        ImGui.SetNextWindowPos(position, ImGuiCond.Always, pivot);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        ImGui.Begin(
            $"stream-button-{id}",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing);
        ImGui.PopStyleVar(2);
        var clicked = button();
        hovered |= ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.End();
        return clicked;
    }

    // These buttons draw with GetColorU32, which applies the fade.

    /// <summary>A translucent dark circle with an icon, readable over any picture.</summary>
    private static unsafe bool RoundButton(string icon, string tooltip)
    {
        var (center, radius, clicked, hovered, held) = CircleItem("button");
        ImGui.SetItemTooltip(tooltip);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0, 0, 0, held ? 0.8f : hovered ? 0.7f : 0.5f)), 48);
        drawList.AddCircle(center, radius - Theme.Dp(0.5f), ImGui.GetColorU32(Theme.WithAlpha(Theme.Text, 0.1f)), 48, Theme.Dp(1));
        ImGui.PushFont(Theme.Regular, 20);
        var extent = ImGui.CalcTextSize(icon);
        ImGui.PopFont();
        var color = hovered ? Theme.Text : Theme.WithAlpha(Theme.Text, 0.85f);
        drawList.AddText(Theme.Regular.Handle, Theme.Dp(20), center - extent / 2, ImGui.GetColorU32(color), icon);
        return clicked;
    }

    /// <summary>A white button with a grey "PS", like the controller's button.</summary>
    private static unsafe bool PsButton()
    {
        var (center, radius, clicked, hovered, held) = CircleItem("ps");
        ImGui.SetItemTooltip("PS button");
        var drawList = ImGui.GetWindowDrawList();
        var fill = held ? Theme.AccentActive : hovered ? Theme.AccentHover : Theme.Accent;
        drawList.AddCircleFilled(center + Theme.Dp(0, 2), radius + Theme.Dp(1), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.3f)), 48);
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fill), 48);
        ImGui.PushFont(Theme.SemiBold, Theme.SmallSize);
        var extent = ImGui.CalcTextSize("PS");
        ImGui.PopFont();
        drawList.AddText(Theme.SemiBold.Handle, Theme.Dp(Theme.SmallSize), center - extent / 2, ImGui.GetColorU32(Theme.TextFaint), "PS");
        return clicked;
    }

    private static (Vector2 Center, float Radius, bool Clicked, bool Hovered, bool Held) CircleItem(string id)
    {
        var size = Theme.Dp(ButtonSize);
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size));
        return (position + new Vector2(size / 2), size / 2, clicked, ImGui.IsItemHovered(), ImGui.IsItemActive());
    }
}
