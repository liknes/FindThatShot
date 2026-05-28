using System.IO;
using System.Windows;
using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using VideoArchiveManager.App.ViewModels;
using VideoArchiveManager.App.Views;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data;
using VideoArchiveManager.Data.Services;

namespace VideoArchiveManager.App;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    public static IHost? Host { get; private set; }

    public static bool IsPlayerAvailable { get; private set; }

    public static string? PlayerInitError { get; private set; }

    public static T GetService<T>() where T : class
    {
        if (Host == null) throw new InvalidOperationException("Host not initialized");
        return Host.Services.GetRequiredService<T>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppSettings.DefaultBaseDirectory);
        Directory.CreateDirectory(AppSettings.DefaultThumbnailDirectory);

        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            IsPlayerAvailable = true;
        }
        catch (Exception ex)
        {
            IsPlayerAvailable = false;
            PlayerInitError = $"VLC initialization failed: {ex.Message}";
        }

        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            })
            .ConfigureServices((ctx, services) =>
            {
                var appSettings = new AppSettings();
                ctx.Configuration.GetSection("AppSettings").Bind(appSettings);
                var store = new JsonSettingsStore(appSettings);

                services.AddSingleton<ISettingsStore>(store);
                services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Current);

                services.AddDbContextFactory<VideoArchiveDbContext>(options =>
                {
                    var dbPath = store.Current.EffectiveDatabasePath;
                    var dir = Path.GetDirectoryName(dbPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    options.UseSqlite($"Data Source={dbPath}");
                });

                if (IsPlayerAvailable)
                {
                    services.AddSingleton(sp =>
                    {
                        try
                        {
                            return new LibVLC();
                        }
                        catch (Exception ex)
                        {
                            IsPlayerAvailable = false;
                            PlayerInitError = $"Failed to create LibVLC instance: {ex.Message}";
                            throw;
                        }
                    });
                }

                services.AddSingleton<IFileSystemService, FileSystemService>();
                services.AddSingleton<IFfprobeService, FfprobeService>();
                services.AddSingleton<IThumbnailService, ThumbnailService>();
                services.AddSingleton<ITagService, TagService>();
                services.AddSingleton<ISearchService, SearchService>();
                services.AddSingleton<IVideoScannerService, VideoScannerService>();
                services.AddSingleton<IVideoLibraryService, VideoLibraryService>();

                services.AddTransient<MainViewModel>();
                services.AddTransient<VideoDetailViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<BulkEditViewModel>();

                services.AddTransient<MainWindow>();

                services.AddLogging(b =>
                {
                    b.AddDebug();
                    b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
                });
            })
            .Build();

        try
        {
            await using var ctx = await Host.Services
                .GetRequiredService<IDbContextFactory<VideoArchiveDbContext>>()
                .CreateDbContextAsync();
            await ctx.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize the database:\n\n{ex.Message}",
                "Video Archive Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }
        base.OnExit(e);
    }
}
