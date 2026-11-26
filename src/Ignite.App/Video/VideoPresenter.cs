using System.Runtime.InteropServices;
using Hexa.NET.SDL3;
using Ignite.Media;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Ignite.App.Video;

/// <summary>
/// Hands the newest decoded picture from the decoder thread to the render thread and draws it
/// letterboxed. Older pictures are dropped so presentation never lags behind decoding.
/// GPU pictures are copied into the SDL texture inside the GPU; CPU pictures are uploaded.
/// </summary>
internal sealed unsafe class VideoPresenter : IVideoFrameSink, IDisposable
{
    private const string TexturePointerProperty = "SDL.texture.d3d11.texture";
    private const string CreateFormatProperty = "SDL.texture.create.format";
    private const string CreateAccessProperty = "SDL.texture.create.access";
    private const string CreateWidthProperty = "SDL.texture.create.width";
    private const string CreateHeightProperty = "SDL.texture.create.height";
    private const string CreateColorspaceProperty = "SDL.texture.create.colorspace";

    private readonly SDLRendererPtr _renderer;
    private readonly ID3D11Device? _device;
    private readonly ID3D11DeviceContext? _context;
    private readonly Lock _lock = new();
    private PendingFrame? _pending;
    private PendingFrame? _spare;
    private GpuVideoFrame? _pendingGpu;
    private SDLTexturePtr _texture;
    private ID3D11Resource? _textureResource;
    private (int Width, int Height, VideoPixelFormat Format, VideoColorSpace ColorSpace) _textureShape;

    /// <param name="d3d11Device">The renderer's ID3D11Device, or zero when it is not Direct3D 11.</param>
    public VideoPresenter(SDLRendererPtr renderer, nint d3d11Device)
    {
        _renderer = renderer;
        if (d3d11Device == 0 || !OperatingSystem.IsWindows())
            return;

        // SDL creates this device single-threaded: everything that touches it, including the
        // decoder when it shares the device, must run on the render thread.
        Marshal.AddRef(d3d11Device);
        _device = new ID3D11Device(d3d11Device);
        _context = _device.ImmediateContext;
    }

    /// <summary>Device to share with the decoder so frames stay on the GPU, or zero.</summary>
    public nint SharedDevice => _device?.NativePointer ?? 0;

    public long FramesPresented { get; private set; }

    public (int Width, int Height) VideoSize => (_textureShape.Width, _textureShape.Height);

    /// <summary>Called on the render thread (the decoder shares its device); keeps only the newest GPU picture.</summary>
    public void OnGpuFrame(GpuVideoFrame frame)
    {
        GpuVideoFrame? replaced;
        lock (_lock)
        {
            replaced = _pendingGpu;
            _pendingGpu = frame;
        }

        replaced?.Dispose();
    }

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
        GpuVideoFrame? gpuFrame;
        lock (_lock)
        {
            frame = _pending;
            _pending = null;
            gpuFrame = _pendingGpu;
            _pendingGpu = null;
        }

        if (gpuFrame is not null)
        {
            using (gpuFrame)
            {
                if (CopyGpuFrame(gpuFrame))
                    FramesPresented++;
            }
        }
        else if (frame is not null)
        {
            Upload(frame);
            lock (_lock)
                _spare ??= frame;
            FramesPresented++;
        }

        if (_texture.IsNull)
            return;

        var (width, height, _, _) = _textureShape;
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
        GpuVideoFrame? gpuFrame;
        lock (_lock)
        {
            _pending = null;
            gpuFrame = _pendingGpu;
            _pendingGpu = null;
        }

        gpuFrame?.Dispose();
        DestroyTexture();
    }

    public void Dispose()
    {
        Clear();
        _context?.Dispose();
        _device?.Dispose();
    }

    private bool CopyGpuFrame(GpuVideoFrame frame)
    {
        if (_context is null || !EnsureTexture(frame.Width, frame.Height, frame.Format, frame.ColorSpace, SDLTextureAccess.Static))
            return false;

        if (_textureResource is null)
        {
            var pointer = (nint)SDL.GetPointerProperty(SDL.GetTextureProperties(_texture), TexturePointerProperty, null);
            if (pointer == 0)
                return false;
            Marshal.AddRef(pointer);
            _textureResource = new ID3D11Resource(pointer);
        }

        Marshal.AddRef(frame.Texture);
        using var source = new ID3D11Resource(frame.Texture);
        SDL.FlushRenderer(_renderer);
        _context.CopySubresourceRegion(_textureResource, 0, 0, 0, 0, source, (uint)frame.ArrayIndex, new Box(0, 0, 0, frame.Width, frame.Height, 1));
        return true;
    }

    private void Upload(PendingFrame frame)
    {
        if (!EnsureTexture(frame.Width, frame.Height, frame.Format, frame.ColorSpace, SDLTextureAccess.Streaming))
            return;

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

    private bool EnsureTexture(int width, int height, VideoPixelFormat format, VideoColorSpace colorSpace, SDLTextureAccess access)
    {
        var shape = (width, height, format, colorSpace);
        if (!_texture.IsNull && shape == _textureShape)
            return true;

        DestroyTexture();
        var pixelFormat = format switch
        {
            VideoPixelFormat.Nv12 => SDLPixelFormat.Nv12,
            VideoPixelFormat.P010 => SDLPixelFormat.P010,
            _ => SDLPixelFormat.Iyuv,
        };

        // The colorspace must be explicit: SDL defaults YUV textures to BT.601 and P010 to an RGB
        // HDR10 colorspace, which its Direct3D 11 renderer refuses for YUV data.
        var sdlColorspace = colorSpace == VideoColorSpace.Bt2020Pq ? SDLColorspace.Bt2020Limited : SDLColorspace.Bt709Limited;
        var properties = SDL.CreateProperties();
        SDL.SetNumberProperty(properties, CreateFormatProperty, (long)pixelFormat);
        SDL.SetNumberProperty(properties, CreateAccessProperty, (long)access);
        SDL.SetNumberProperty(properties, CreateWidthProperty, width);
        SDL.SetNumberProperty(properties, CreateHeightProperty, height);
        SDL.SetNumberProperty(properties, CreateColorspaceProperty, (long)sdlColorspace);
        _texture = SDL.CreateTextureWithProperties(_renderer, properties);
        SDL.DestroyProperties(properties);
        _textureShape = shape;
        return !_texture.IsNull;
    }

    private void DestroyTexture()
    {
        _textureResource?.Dispose();
        _textureResource = null;
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
        public VideoColorSpace ColorSpace { get; private set; }

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
            ColorSpace = frame.ColorSpace;
        }
    }
}
