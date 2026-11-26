using System.Runtime.InteropServices;
using Concentus;

namespace Ignite.Media.Tests;

public class MediaTests
{
    [FfmpegTheory]
    [InlineData(VideoCodecKind.H264, "testsrc-320x240.h264", HardwareAcceleration.None)]
    [InlineData(VideoCodecKind.Hevc, "testsrc-320x240.hevc", HardwareAcceleration.None)]
    [InlineData(VideoCodecKind.Hevc, "testsrc-320x240-pq.hevc", HardwareAcceleration.None)]
    [InlineData(VideoCodecKind.H264, "testsrc-320x240.h264", HardwareAcceleration.Auto)]
    [InlineData(VideoCodecKind.Hevc, "testsrc-320x240.hevc", HardwareAcceleration.Auto)]
    [InlineData(VideoCodecKind.Hevc, "testsrc-320x240-pq.hevc", HardwareAcceleration.Auto)]
    public void VideoDecoder_DecodesAccessUnits(VideoCodecKind codec, string fixture, HardwareAcceleration acceleration)
    {
        var stream = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var units = FfmpegTestEnvironment.SplitAccessUnits(stream, codec);
        using var decoder = new VideoDecoder(codec, new VideoDecoderOptions(acceleration));
        var hdr = fixture.Contains("-pq", StringComparison.Ordinal);

        var frames = new List<(int Width, int Height, VideoPixelFormat Format, VideoColorSpace ColorSpace, int Planes, bool Hardware)>();
        foreach (var unit in units)
            decoder.Decode(unit, new DelegateSink(f => frames.Add((f.Width, f.Height, f.Format, f.ColorSpace, f.Planes.Count, f.HardwareDecoded))));

        Assert.Equal(10, units.Count);
        Assert.InRange(frames.Count, 9, 10);
        Assert.All(frames, f =>
        {
            Assert.Equal(320, f.Width);
            Assert.Equal(240, f.Height);
            Assert.Equal(f.Format == VideoPixelFormat.Yuv420P ? 3 : 2, f.Planes);
            Assert.Equal(decoder.HardwareDevice is not null, f.Hardware);
            if (hdr)
                Assert.Equal(VideoPixelFormat.P010, f.Format);
            Assert.Equal(hdr ? VideoColorSpace.Bt2020Pq : VideoColorSpace.Bt709, f.ColorSpace);
        });
        if (acceleration == HardwareAcceleration.None || !VideoDecoder.SupportsHardware(codec))
            Assert.Null(decoder.HardwareDevice);
    }

    [Fact]
    public void SupportsHardware_IsFalseOnlyForH264OnWindows()
    {
        Assert.True(VideoDecoder.SupportsHardware(VideoCodecKind.Hevc));
        Assert.Equal(!OperatingSystem.IsWindows(), VideoDecoder.SupportsHardware(VideoCodecKind.H264));
    }

    [FfmpegFact]
    public void VideoDecoder_SoftwareP010KeepsTenBitSamples()
    {
        var stream = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testsrc-320x240-pq.hevc"));
        using var decoder = new VideoDecoder(VideoCodecKind.Hevc, new VideoDecoderOptions(HardwareAcceleration.None));
        ushort[]? luma = null;

        foreach (var unit in FfmpegTestEnvironment.SplitAccessUnits(stream, VideoCodecKind.Hevc))
            decoder.Decode(unit, new DelegateSink(f => luma ??= MemoryMarshal.Cast<byte, ushort>(f.Planes[0].Data.Span).ToArray()));

        Assert.NotNull(luma);
        Assert.All(luma, sample => Assert.Equal(0, sample & 0x3F));
        Assert.Contains(luma, sample => (sample >> 6) is > 64 and < 940);
    }

    [FfmpegFact]
    public void VideoDecoder_IgnoresGarbage()
    {
        using var decoder = new VideoDecoder(VideoCodecKind.H264, new VideoDecoderOptions(HardwareAcceleration.None));
        var frames = 0;

        decoder.Decode([0, 0, 0, 1, 0xFF, 0xFF, 0x12], new DelegateSink(_ => frames++));

        Assert.Equal(0, frames);
    }

    [Fact]
    public void OpusAudioDecoder_DecodesEncodedTone()
    {
        const int rate = 48000, channels = 2, frameSize = 480;
        using var encoder = OpusCodecFactory.CreateEncoder(rate, channels, Concentus.Enums.OpusApplication.OPUS_APPLICATION_AUDIO);
        using var decoder = new OpusAudioDecoder(rate, channels);
        var pcm = new short[frameSize * channels];
        for (var i = 0; i < frameSize; i++)
            pcm[2 * i] = pcm[(2 * i) + 1] = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000);
        var packet = new byte[1500];

        var length = encoder.Encode(pcm, frameSize, packet, packet.Length);
        var decoded = decoder.Decode(packet.AsSpan(0, length));

        Assert.Equal(frameSize * channels, decoded.Length);
        Assert.Equal(frameSize * channels, decoder.Conceal(frameSize).Length);
    }
}
