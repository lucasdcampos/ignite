using FFmpeg.AutoGen;

namespace Ignite.Media;

/// <summary>Locates and loads the shared FFmpeg libraries once per process.</summary>
public static class FfmpegLibrary
{
    public const string PathEnvironmentVariable = "IGNITE_FFMPEG_PATH";

    private static readonly Lock Gate = new();
    private static bool _loaded;

    /// <summary>
    /// Loads FFmpeg from <paramref name="directory"/>, IGNITE_FFMPEG_PATH or the application
    /// directory, in that order. Returns the FFmpeg version string.
    /// </summary>
    public static string Load(string? directory = null)
    {
        lock (Gate)
        {
            if (!_loaded)
            {
                ffmpeg.RootPath = directory
                    ?? Environment.GetEnvironmentVariable(PathEnvironmentVariable)
                    ?? AppContext.BaseDirectory;
                DynamicallyLoadedBindings.Initialize();
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                _loaded = true;
            }

            return ffmpeg.av_version_info();
        }
    }

    internal static string ErrorText(int error)
    {
        unsafe
        {
            const int size = 256;
            var buffer = stackalloc byte[size];
            ffmpeg.av_strerror(error, buffer, size);
            return new string((sbyte*)buffer);
        }
    }
}
