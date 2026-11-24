using Ignite.Core.Storage;

namespace Ignite.Core.Settings;

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(AppPaths paths)
    {
        _path = paths.SettingsFile;
        Current = JsonFile.Load(_path, () => new AppSettings());
    }

    public AppSettings Current { get; private set; }

    public void Save(AppSettings settings)
    {
        Current = settings;
        JsonFile.Save(_path, settings, restrictAccess: true);
    }
}
