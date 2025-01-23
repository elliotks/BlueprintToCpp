using CUE4Parse.Compression;
using Main.ViewModels;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace Main.Service;

public static class ApplicationService
{
    public static CUE4ParseViewModel CUE4Parse = new();

    public static async Task Initialize()
    {
#if DEBUG
        Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).MinimumLevel.Debug().CreateLogger();
#else
        Log.Loger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
#endif

        await InitOodle().ConfigureAwait(false);
    }

    public static async Task InitOodle()
    {
        var oodlePath = Path.Combine(Environment.CurrentDirectory, OodleHelper.OODLE_DLL_NAME);
        if (!File.Exists(oodlePath)) await OodleHelper.DownloadOodleDllAsync(oodlePath).ConfigureAwait(false);
        OodleHelper.Initialize(oodlePath);
    }
}
