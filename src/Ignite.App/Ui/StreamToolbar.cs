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

/// <summary>The bar at the bottom of the stream: fullscreen, the PS button (only when playing) and the stream settings.</summary>
internal static class StreamToolbar
{
    private const float ButtonSize = 48;
    private const float PsButtonSize = 60;
    private const float Padding = 10;
    private const float BottomMargin = 28;

    /// <summary>Distance from the bottom of the window to the top of the bar, in design units.</summary>
    public const float Clearance = BottomMargin + PsButtonSize + Padding * 2;

    /// <summary>
    /// Draws the bar. It never takes keyboard or gamepad focus, so clicking it leaves the
    /// controller with the game.
    /// </summary>
    public static ToolbarAction Draw(bool fullscreen, bool playing, float opacity, out bool hovered)
    {
        var viewport = ImGui.GetMainViewport();
        var anchor = viewport.WorkPos + new Vector2(viewport.WorkSize.X / 2, viewport.WorkSize.Y - Theme.Dp(BottomMargin));
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(0.5f, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, opacity);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(Padding + 6, Padding));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Theme.Dp(PsButtonSize / 2 + Padding));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Theme.Dp(18, 0));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.WithAlpha(Theme.SurfaceRaised, 0.92f));
        ImGui.Begin(
            "stream-toolbar",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing);

        var action = ToolbarAction.None;
        var top = ImGui.GetCursorPosY();
        var offset = Theme.Dp((PsButtonSize - ButtonSize) / 2);
        ImGui.SetCursorPosY(top + offset);
        if (Widgets.IconButton(fullscreen ? Icons.Minimize : Icons.Maximize, "fullscreen", fullscreen ? "Exit fullscreen (F11)" : "Fullscreen (F11)", ButtonSize))
            action = ToolbarAction.Fullscreen;
        ImGui.SameLine();
        if (playing)
        {
            ImGui.SetCursorPosY(top);
            if (PsButton())
                action = ToolbarAction.PsButton;
            ImGui.SameLine();
        }

        ImGui.SetCursorPosY(top + offset);
        if (Widgets.IconButton(Icons.Settings, "settings", "Stream settings (F1)", ButtonSize))
            action = ToolbarAction.Settings;

        hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(4);
        return action;
    }

    private static unsafe bool PsButton()
    {
        var size = Theme.Dp(PsButtonSize);
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("ps", new Vector2(size));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        ImGui.SetItemTooltip("PS button");

        // GetColorU32 applies the bar's fade.
        var drawList = ImGui.GetWindowDrawList();
        var center = position + new Vector2(size / 2);
        var fill = held ? Theme.AccentActive : hovered ? Theme.AccentHover : Theme.Accent;
        drawList.AddCircleFilled(center, size / 2, ImGui.GetColorU32(fill), 48);
        ImGui.PushFont(Theme.SemiBold, Theme.HeadingSize);
        var extent = ImGui.CalcTextSize("PS");
        ImGui.PopFont();
        drawList.AddText(Theme.SemiBold.Handle, Theme.Dp(Theme.HeadingSize), center - extent / 2, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), "PS");
        return clicked;
    }
}
