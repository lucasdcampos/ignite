using Hexa.NET.SDL3;
using Ignite.Media;

namespace Ignite.App.Video;

/// <summary>
/// Hands the newest decoded picture from the decoder thread to the render thread and draws it
/// letterboxed. Older pictures are dropped so presentation never lags behind decoding.
/// </summary>
internal sealed unsafe class VideoPresenter : IVideoFrameSink, IDisposable
{
    private readonly SDLRendererPtr _renderer;
    private readonly Lock _lock = new();
    private PendingFrame? _pending;
    private PendingFrame? _spare;
    private SDLTexturePtr _texture;
    private (int Width, int Height, VideoPixelFormat Format) _textureShape;

    public VideoPresenter(SDLRendererPtr renderer) => _renderer = renderer;

    public long FramesPresented { get; private set; }

    public (int Width, int Height) VideoSize => (_textureShape.Width, _textureShape.Height);

    public void OnGpuFrame(GpuVideoFrame frame) => frame.Dispose();

    /// <summary>Called on the decoder thread; copies the picture.</summary>
    public void OnCpuFrame(DecodedVideoFrame frame)
    {
        PendingFrame target;
        lock (_lock)
        {
            target = _spare ?? new PendingFrame();
            _spare = null;
        }

        target.CopyFrom(frame);

        lock (_lock)
        {
            _spare = _pending;
            _pending = target;
        }
    }

    /// <summary>Called on the render thread.</summary>
    public void Draw(int outputWidth, int outputHeight)
    {
        PendingFrame? frame;
        lock (_lock)
        {
            frame = _pending;
            _pending = null;
        }

        if (frame is not null)
        {
            Upload(frame);
            lock (_lock)
                _spare ??= frame;
            FramesPresented++;
        }

        if (_texture.IsNull)
            return;

        var (width, height, _) = _textureShape;
        var scale = Math.Min((float)outputWidth / width, (float)outputHeight / height);
        var destination = new SDLFRect
        {
            W = width * scale,
            H = height * scale,
            X = (outputWidth - (width * scale)) / 2,
            Y = (outputHeight - (height * scale)) / 2,
        };
        SDL.RenderTexture(_renderer, _texture, (SDLFRect*)null, &destination);
    }

    public void Clear()
    {
        lock (_lock)
            _pending = null;
        DestroyTexture();
    }

    public void Dispose() => DestroyTexture();

    private void Upload(PendingFrame frame)
    {
        var shape = (frame.Width, frame.Height, frame.Format);
        if (_texture.IsNull || shape != _textureShape)
        {
            DestroyTexture();
            var format = frame.Format switch
            {
                VideoPixelFormat.Nv12 => SDLPixelFormat.Nv12,
                VideoPixelFormat.P010 => SDLPixelFormat.P010,
                _ => SDLPixelFormat.Iyuv,
            };
            _texture = SDL.CreateTexture(_renderer, format, SDLTextureAccess.Streaming, frame.Width, frame.Height);
            _textureShape = shape;
            if (_texture.IsNull)
                return;
        }

        fixed (byte* data = frame.Buffer)
        {
            var planes = frame.Planes;
            if (frame.Format == VideoPixelFormat.Yuv420P)
            {
                SDL.UpdateYUVTexture(_texture, (SDLRect*)null,
                    data + planes[0].Offset, planes[0].Stride,
                    data + planes[1].Offset, planes[1].Stride,
                    data + planes[2].Offset, planes[2].Stride);
            }
            else
            {
                SDL.UpdateNVTexture(_texture, (SDLRect*)null,
                    data + planes[0].Offset, planes[0].Stride,
                    data + planes[1].Offset, planes[1].Stride);
            }
        }
    }

    private void DestroyTexture()
    {
        if (_texture.IsNull)
            return;
        SDL.DestroyTexture(_texture);
        _texture = default;
    }

    private sealed class PendingFrame
    {
        public byte[] Buffer { get; private set; } = [];
        public List<(int Offset, int Stride)> Planes { get; } = [];
        public int Width { get; private set; }
        public int Height { get; private set; }
        public VideoPixelFormat Format { get; private set; }

        public void CopyFrom(DecodedVideoFrame frame)
        {
            var total = frame.Planes.Sum(p => p.Data.Length);
            if (Buffer.Length < total)
                Buffer = new byte[total];

            Planes.Clear();
            var offset = 0;
            foreach (var plane in frame.Planes)
            {
                plane.Data.Span.CopyTo(Buffer.AsSpan(offset));
                Planes.Add((offset, plane.Stride));
                offset += plane.Data.Length;
            }

            Width = frame.Width;
            Height = frame.Height;
            Format = frame.Format;
        }
    }
}
