using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using ImGuiImplSDL3 = Hexa.NET.ImGui.Backends.SDL3.ImGuiImplSDL3;
using Hexa.NET.SDL3;
using ImGuiSdlEvent = Hexa.NET.ImGui.Backends.SDL3.SDLEvent;
using ImGuiSdlRenderer = Hexa.NET.ImGui.Backends.SDL3.SDLRenderer;
using ImGuiSdlWindow = Hexa.NET.ImGui.Backends.SDL3.SDLWindow;
using SdlEvent = Hexa.NET.SDL3.SDLEvent;

namespace Ignite.App.Platform;

/// <summary>Owns the SDL window, renderer and the Dear ImGui context.</summary>
internal sealed unsafe class SdlHost : IDisposable
{
    private readonly ImGuiContextPtr _imgui;
    private bool _vsync;

    public SdlHost(string title, int width, int height)
    {
        if (!SDL.Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_GAMEPAD | SDL.SDL_INIT_EVENTS))
            throw new InvalidOperationException($"SDL initialisation failed: {LastError}");

        Window = SDL.CreateWindow(title, width, height, (ulong)(SDLWindowFlags.Resizable | SDLWindowFlags.HighPixelDensity));
        if (Window.IsNull)
            throw new InvalidOperationException($"Could not create the window: {LastError}");

        Renderer = SDL.CreateRenderer(Window, (byte*)null);
        if (Renderer.IsNull)
            throw new InvalidOperationException($"Could not create the renderer: {LastError}");
        SetVSync(true);

        _imgui = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imgui);
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.NavEnableGamepad;
        io.IniFilename = null;
        ImGui.StyleColorsDark();

        ImGuiImplSDL3.SetCurrentContext(_imgui);
        ImGuiImplSDL3.InitForSDLRenderer((ImGuiSdlWindow*)Window.Handle, (ImGuiSdlRenderer*)Renderer.Handle);
        ImGuiImplSDL3.SDLRenderer3Init((ImGuiSdlRenderer*)Renderer.Handle);
    }

    public SDLWindowPtr Window { get; }

    public SDLRendererPtr Renderer { get; }

    public static string LastError => Marshal.PtrToStringUTF8((nint)SDL.GetError()) ?? "unknown error";

    public string RendererName => Marshal.PtrToStringUTF8((nint)SDL.GetRendererName(Renderer)) ?? "unknown";

    public (int Width, int Height) OutputSize
    {
        get
        {
            int width, height;
            SDL.GetCurrentRenderOutputSize(Renderer, &width, &height);
            return (width, height);
        }
    }

    public void SetVSync(bool enabled)
    {
        if (_vsync == enabled && enabled)
            return;
        _vsync = enabled;
        SDL.SetRenderVSync(Renderer, enabled ? 1 : 0);
    }

    public void SetFullscreen(bool fullscreen) => SDL.SetWindowFullscreen(Window, fullscreen);

    /// <summary>Polls events, forwarding them to ImGui; returns false when the user closes the window.</summary>
    public static bool PumpEvents(Action<SdlEvent> handler)
    {
        SdlEvent e;
        var running = true;
        while (SDL.PollEvent(&e))
        {
            ImGuiImplSDL3.ProcessEvent((ImGuiSdlEvent*)&e);
            if ((SDLEventType)e.Type == SDLEventType.Quit)
                running = false;
            handler(e);
        }

        return running;
    }

    public void BeginFrame()
    {
        ImGuiImplSDL3.SDLRenderer3NewFrame();
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();
        SDL.SetRenderDrawColor(Renderer, 12, 12, 16, 255);
        SDL.RenderClear(Renderer);
    }

    public void EndFrame(string? screenshotPath = null)
    {
        ImGui.Render();
        ImGuiImplSDL3.SDLRenderer3RenderDrawData(ImGui.GetDrawData(), (ImGuiSdlRenderer*)Renderer.Handle);
        if (screenshotPath is not null)
            SaveScreenshot(screenshotPath);
        SDL.RenderPresent(Renderer);
    }

    public static void OpenUrl(string url) => SDL.OpenURL(url);

    public static string? ClipboardText()
    {
        var text = SDL.GetClipboardText();
        if (text == null)
            return null;
        var value = Marshal.PtrToStringUTF8((nint)text);
        SDL.Free(text);
        return value;
    }

    /// <summary>Saves the current render target as a BMP file (used for automated smoke tests).</summary>
    public bool SaveScreenshot(string path)
    {
        var surface = SDL.RenderReadPixels(Renderer, (SDLRect*)null);
        if (surface.IsNull)
            return false;
        var saved = SDL.SaveBMP(surface, path);
        SDL.DestroySurface(surface);
        return saved;
    }

    public void Dispose()
    {
        ImGuiImplSDL3.SDLRenderer3Shutdown();
        ImGuiImplSDL3.Shutdown();
        ImGui.DestroyContext(_imgui);
        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(Window);
        SDL.Quit();
    }
}
