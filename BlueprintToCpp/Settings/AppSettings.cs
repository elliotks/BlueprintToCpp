using Main.ViewModels;

namespace Main.Settings;

public class AppSettings
{
    public static SettingsViewModel Current = new();

    private static readonly DirectoryInfo SettingsDirectory = new(Path.Combine(Environment.CurrentDirectory, "Output", "Settings"));
}
