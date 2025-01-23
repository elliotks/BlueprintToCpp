using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Main.Settings;

namespace Main.ViewModels;

public class CUE4ParseViewModel
{
    public DefaultFileProvider Provider;

    public void Initialize()
    {
        Provider = new DefaultFileProvider(AppSettings.Current.GameDirectory, SearchOption.AllDirectories, true, new VersionContainer(AppSettings.Current.GameVersion));

        Provider.Initialize();

        // aes and mappings shit later
    }
}
