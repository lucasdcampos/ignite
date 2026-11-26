using Hexa.NET.ImGui;
using Ignite.Core.Settings;
using Ignite.Protocol.Session;

namespace Ignite.App.Ui;

/// <summary>Resolution, frame rate, video format and bitrate controls.</summary>
internal static class QualityEditor
{
    private static readonly string[] ResolutionNames = ["360p", "540p", "720p", "1080p"];
    private static readonly string[] FpsNames = ["30 fps", "60 fps"];
    private static readonly int[] FpsValues = [30, 60];
    private static readonly string[] CodecNames = ["H.264", "HEVC", "HEVC HDR"];

    /// <summary>Draws the controls; returns true when the user changed <paramref name="quality"/>.</summary>
    public static bool Draw(ref StreamQuality quality)
    {
        var resolution = (int)quality.Resolution;
        var fps = Math.Max(0, Array.IndexOf(FpsValues, quality.Fps));
        var codec = (int)quality.Codec - 1;
        var automatic = quality.BitrateKbps is null;
        var mbps = quality.EffectiveBitrateKbps / 1000;

        Widgets.Label("Resolution");
        var changed = Widgets.Segmented("resolution", ResolutionNames, ref resolution);
        Widgets.Label("Frame rate");
        changed |= Widgets.Segmented("fps", FpsNames, ref fps);
        Widgets.Label("Video format");
        changed |= Widgets.Segmented("codec", CodecNames, ref codec);
        Widgets.Text("HEVC gives a better picture for the same bandwidth. HDR needs an HDR display.", Theme.SmallSize, Theme.TextFaint);
        Widgets.Space(4);
        changed |= Widgets.Toggle("Automatic bitrate", "Picks a bitrate that suits the resolution.", ref automatic);
        if (!automatic)
        {
            ImGui.SetNextItemWidth(Widgets.Fill);
            changed |= ImGui.SliderInt("##bitrate", ref mbps, 2, 100, "%d Mbps");
        }

        if (changed)
            quality = new StreamQuality((Resolution)resolution, FpsValues[fps], (VideoCodec)(codec + 1), automatic ? null : mbps * 1000);
        Widgets.Text($"Uses up to {quality.EffectiveBitrateKbps / 1000} Mbps of your network.", Theme.SmallSize, Theme.TextMuted);
        return changed;
    }

    /// <summary>A one-line summary, for example "1080p · 60 fps · HEVC · 15 Mbps".</summary>
    public static string Describe(StreamQuality quality) =>
        $"{ResolutionNames[(int)quality.Resolution]}  ·  {quality.Fps} fps  ·  {CodecNames[(int)quality.Codec - 1]}  ·  {quality.EffectiveBitrateKbps / 1000} Mbps";
}
