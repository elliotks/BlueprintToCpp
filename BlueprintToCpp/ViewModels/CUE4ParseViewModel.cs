using System;
using BlueRange.Services;
using BlueRange.Settings;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Serilog;

namespace BlueRange.ViewModels;

public class CUE4ParseViewModel
{
    public DefaultFileProvider Provider;

    public CUE4ParseViewModel()
    {
        Provider = new DefaultFileProvider(AppSettings.Current.GameDirectory, SearchOption.AllDirectories, true, new VersionContainer(AppSettings.Current.GameVersion));
    }

    public async Task InitializeAsync()
    {
        if (Provider.UnloadedVfs.Count == 0)
            Provider = new DefaultFileProvider(AppSettings.Current.GameDirectory, SearchOption.AllDirectories, true, new VersionContainer(AppSettings.Current.GameVersion));

        Provider.MappingsContainer = new FileUsmapTypeMappingsProvider(AppSettings.Current.UsmapPath);
        Provider.Initialize();

        var aes = await ApplicationService.Api.FortniteCentral.GetAesAsync().ConfigureAwait(false);
        await Provider.SubmitKeyAsync(new FGuid(), new FAesKey(aes.MainKey)).ConfigureAwait(false);

        foreach (var dynamicKey in aes.DynamicKeys)
            await Provider.SubmitKeyAsync(new FGuid(dynamicKey.Guid), new FAesKey(dynamicKey.Key)).ConfigureAwait(false);

        var mountedPackages = await Provider.MountAsync().ConfigureAwait(false);
        var localizedStrings = Provider.LoadLocalization();

        Log.Information("Mounted {0} packages", mountedPackages);
        Log.Information("Loaded {0} localized strings", localizedStrings);
    }
}
