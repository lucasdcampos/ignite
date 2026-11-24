using Ignite.Protocol.Session;

namespace Ignite.Core.Settings;

/// <summary>Named quality levels offered to users who do not want to tune each option.</summary>
public enum QualityPreset
{
    Performance,
    Balanced,
    Quality,
}

public static class QualityPresets
{
    public static IReadOnlyList<QualityPreset> All { get; } = [QualityPreset.Performance, QualityPreset.Balanced, QualityPreset.Quality];

    public static StreamQuality ToQuality(this QualityPreset preset) => preset switch
    {
        QualityPreset.Performance => new StreamQuality(Resolution.P720, 60, VideoCodec.H264),
        QualityPreset.Balanced => new StreamQuality(Resolution.P1080, 60, VideoCodec.Hevc),
        QualityPreset.Quality => new StreamQuality(Resolution.P1440, 60, VideoCodec.Hevc),
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    /// <summary>The preset these settings correspond to, or null when they were customized.</summary>
    public static QualityPreset? Match(StreamQuality quality)
    {
        foreach (var preset in All)
        {
            if (preset.ToQuality() == quality)
                return preset;
        }

        return null;
    }
}
