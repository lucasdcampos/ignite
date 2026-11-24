using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;

namespace Ignite.App.Ui;

/// <summary>Colors, type scale and Dear ImGui style of the launcher.</summary>
internal static class Theme
{
    public const float BodySize = 17;
    public const float SmallSize = 15;
    public const float HeadingSize = 21;
    public const float TitleSize = 32;
    public const float HeroSize = 64;

    public static readonly Vector4 Background = Rgb(0x0C0E13);
    public static readonly Vector4 BackgroundGlow = Rgb(0x1B1B2A);
    public static readonly Vector4 Surface = Rgb(0x161921);
    public static readonly Vector4 SurfaceHover = Rgb(0x1D212B);
    public static readonly Vector4 SurfaceRaised = Rgb(0x232835);
    public static readonly Vector4 Border = Rgb(0x2A303C);
    public static readonly Vector4 Text = Rgb(0xEDEFF4);
    public static readonly Vector4 TextMuted = Rgb(0xA0A7B6);
    public static readonly Vector4 TextFaint = Rgb(0x6B7283);
    public static readonly Vector4 Accent = Rgb(0xEC5B24);
    public static readonly Vector4 AccentHover = Rgb(0xFF7240);
    public static readonly Vector4 AccentActive = Rgb(0xD04B18);
    public static readonly Vector4 AccentSoft = Rgb(0xEC5B24, 0.16f);
    public static readonly Vector4 Success = Rgb(0x3DD68C);
    public static readonly Vector4 Warning = Rgb(0xF5B841);
    public static readonly Vector4 Danger = Rgb(0xF26464);
    public static readonly Vector4 Shade = Rgb(0x000000, 0.6f);

    /// <summary>Zero-terminated range list; the atlas keeps the pointer, so it is never freed.</summary>
    private static readonly unsafe uint* PrivateUseArea = CreateRanges(0xE000, 0xF8FF);

    public static ImFontPtr Regular { get; private set; }

    public static ImFontPtr SemiBold { get; private set; }

    /// <summary>Converts a size in design units to pixels at the current content scale.</summary>
    public static float Dp(float value) => value * ImGui.GetStyle().FontScaleDpi;

    public static Vector2 Dp(float x, float y) => new(Dp(x), Dp(y));

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static Vector4 WithAlpha(Vector4 color, float alpha) => color with { W = alpha };

    /// <summary>Loads Inter (text) with Lucide (icons) merged into each weight.</summary>
    public static void LoadFonts(ImGuiIOPtr io)
    {
        var icons = ReadAsset("Lucide.ttf");
        Regular = AddFont(io.Fonts, ReadAsset("Inter-Regular.ttf"), merge: false);
        AddFont(io.Fonts, icons, merge: true);
        SemiBold = AddFont(io.Fonts, ReadAsset("Inter-SemiBold.ttf"), merge: false);
        AddFont(io.Fonts, icons, merge: true);
        io.FontDefault = Regular;
    }

    /// <summary>Resets the style to the theme at the given content scale (1 = 96 dpi).</summary>
    public static void Apply(float scale)
    {
        var style = ImGui.GetStyle();
        ImGui.StyleColorsDark(style);
        style.FontSizeBase = BodySize;
        style.WindowPadding = new Vector2(16, 14);
        style.WindowBorderSize = 0;
        style.ChildBorderSize = 0;
        style.PopupBorderSize = 1;
        style.FrameBorderSize = 0;
        style.WindowRounding = 16;
        style.ChildRounding = 16;
        style.PopupRounding = 12;
        style.FrameRounding = 10;
        style.GrabRounding = 10;
        style.ScrollbarRounding = 10;
        style.FramePadding = new Vector2(14, 10);
        style.ItemSpacing = new Vector2(12, 12);
        style.ItemInnerSpacing = new Vector2(10, 8);
        style.ScrollbarSize = 12;
        style.GrabMinSize = 18;
        style.DisabledAlpha = 0.45f;
        style.ScaleAllSizes(scale);
        style.FontScaleDpi = scale;

        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = Text;
        colors[(int)ImGuiCol.TextDisabled] = TextFaint;
        colors[(int)ImGuiCol.WindowBg] = Background;
        colors[(int)ImGuiCol.ChildBg] = Surface;
        colors[(int)ImGuiCol.PopupBg] = SurfaceRaised;
        colors[(int)ImGuiCol.ModalWindowDimBg] = Shade;
        colors[(int)ImGuiCol.Border] = Border;
        colors[(int)ImGuiCol.FrameBg] = Rgb(0x0F1117);
        colors[(int)ImGuiCol.FrameBgHovered] = Rgb(0x131620);
        colors[(int)ImGuiCol.FrameBgActive] = Rgb(0x151925);
        colors[(int)ImGuiCol.Button] = SurfaceRaised;
        colors[(int)ImGuiCol.ButtonHovered] = Rgb(0x2D3342);
        colors[(int)ImGuiCol.ButtonActive] = Rgb(0x363D4F);
        colors[(int)ImGuiCol.Header] = SurfaceRaised;
        colors[(int)ImGuiCol.HeaderHovered] = Rgb(0x2D3342);
        colors[(int)ImGuiCol.HeaderActive] = Rgb(0x363D4F);
        colors[(int)ImGuiCol.CheckMark] = Accent;
        colors[(int)ImGuiCol.SliderGrab] = Accent;
        colors[(int)ImGuiCol.SliderGrabActive] = AccentHover;
        colors[(int)ImGuiCol.ScrollbarBg] = Rgb(0x000000, 0);
        colors[(int)ImGuiCol.ScrollbarGrab] = Rgb(0x2A303C);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = Rgb(0x353C4B);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = Rgb(0x404859);
        colors[(int)ImGuiCol.Separator] = Border;
        colors[(int)ImGuiCol.TextSelectedBg] = Rgb(0xEC5B24, 0.35f);
        colors[(int)ImGuiCol.NavCursor] = Text;
    }

    private static unsafe ImFontPtr AddFont(ImFontAtlasPtr atlas, byte[] data, bool merge)
    {
        var config = ImGui.ImFontConfig();
        config.MergeMode = merge;
        // Inter has its own glyphs in the private use area, which would hide the icons.
        if (!merge)
            config.GlyphExcludeRanges = PrivateUseArea;
        // Icons sit slightly high next to Inter's x-height.
        config.GlyphOffset = merge ? new Vector2(0, 3) : Vector2.Zero;
        // The atlas owns and frees the copy, so it must come from ImGui's allocator.
        var memory = ImGui.MemAlloc((nuint)data.Length);
        data.CopyTo(new Span<byte>(memory, data.Length));
        var font = atlas.AddFontFromMemoryTTF(memory, data.Length, BodySize, config);
        config.Destroy();
        return font;
    }

    private static unsafe uint* CreateRanges(uint first, uint last)
    {
        var ranges = (uint*)NativeMemory.Alloc(3, sizeof(uint));
        ranges[0] = first;
        ranges[1] = last;
        ranges[2] = 0;
        return ranges;
    }

    private static byte[] ReadAsset(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Ignite.App.Assets.Fonts.{name}")
            ?? throw new InvalidOperationException($"Missing embedded font {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static Vector4 Rgb(uint rgb, float alpha = 1) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
}
