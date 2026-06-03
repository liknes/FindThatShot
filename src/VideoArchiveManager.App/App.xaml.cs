using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using VideoArchiveManager.App.Services;
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

    // EXPERIMENTAL mpv player path. Activates only when libmpv-2.dll is present
    // in tools/mpv/. When on, MainWindow uses the GPU-rendered mpv child-window
    // player instead of FFME (which can't sustain 4K60 through its CPU
    // WriteableBitmap). FFME stays fully wired as the fallback.
    public static bool UseMpvPlayer { get; private set; }

    public static string? MpvDirectory { get; private set; }

    public static T GetService<T>() where T : class
    {
        if (Host == null) throw new InvalidOperationException("Host not initialized");
        return Host.Services.GetRequiredService<T>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Force ModernWpfUI's accent-derived brushes to pick up the warm
        // amber from Theme/Colors.xaml. The XAML brush overrides cover
        // explicit references but ThemeManager owns the runtime accent
        // for built-in templates (e.g. AccentButtonStyle, focus visuals).
        // Keeping both belt-and-braces means the accent is consistent
        // whether a brush is resolved at template-build time or via the
        // ThemeManager.AccentColor property.
        ModernWpf.ThemeManager.Current.AccentColor =
            System.Windows.Media.Color.FromArgb(0xFF, 0xF5, 0xA6, 0x23);

        // Splash window. Shown immediately so the user gets visual feedback
        // while DI / DB migration / FFmpeg probe runs. Dismissed with a
        // fade-out once MainWindow is ready (see end of OnStartup). Failures
        // here must not gate startup — if construction or Show throws, the
        // app proceeds without a splash.
        Views.SplashWindow? splash = null;
        try
        {
            splash = new Views.SplashWindow();
            splash.Show();
        }
        catch
        {
            splash = null;
        }

        Directory.CreateDirectory(AppSettings.DefaultBaseDirectory);
        Directory.CreateDirectory(AppSettings.DefaultThumbnailDirectory);

        // Point FFME at the bundled FFmpeg shared libraries. We co-locate the
        // FFmpeg shared DLLs with FFprobe in tools/ffmpeg/ so the player and
        // the FFprobe metadata pipeline use a single FFmpeg copy. The path
        // is the same one publish.ps1 deploys into the installer; for local
        // debug builds the csproj copies tools/ffmpeg/ into the output dir.
        // FFME loads FFmpeg lazily on first MediaElement use, so we only
        // verify the directory exists here and gate IsPlayerAvailable on it.
        try
        {
            var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
            if (Directory.Exists(ffmpegDir) && File.Exists(Path.Combine(ffmpegDir, "avcodec-62.dll")))
            {
                Unosquare.FFME.Library.FFmpegDirectory = ffmpegDir;

                IsPlayerAvailable = true;
            }
            else
            {
                IsPlayerAvailable = false;
                PlayerInitError =
                    $"FFmpeg shared libraries not found at {ffmpegDir}. " +
                    "The in-app player needs the FFmpeg 8.x shared DLLs " +
                    "(avcodec-62.dll etc.) co-located with ffprobe.exe.";
            }
        }
        catch (Exception ex)
        {
            IsPlayerAvailable = false;
            PlayerInitError = $"FFmpeg initialization failed: {ex.Message}";
        }

        // EXPERIMENTAL: detect a bundled libmpv and, if present, route the
        // in-app player through the GPU-rendered mpv child-window instead of
        // FFME. Drop a self-contained libmpv-2.dll into tools/mpv/ to enable.
        try
        {
            var mpvDir = Path.Combine(AppContext.BaseDirectory, "tools", "mpv");
            if (File.Exists(Path.Combine(mpvDir, "libmpv-2.dll")))
            {
                MpvDirectory = mpvDir;
                Helpers.Player.MpvInterop.Register(mpvDir);
                UseMpvPlayer = true;
            }
        }
        catch
        {
            // Any failure here just leaves UseMpvPlayer false → FFME is used.
            UseMpvPlayer = false;
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
                // IMPORTANT: zero out collection-typed defaults BEFORE Bind.
                // Microsoft.Extensions.Configuration's Bind() APPENDS array
                // values from JSON to whatever's already in the instance,
                // which would otherwise turn the 5 default extensions
                // + 5 appsettings.json extensions into a 10-item list that
                // grows over time. JsonSettingsStore dedupes defensively too,
                // but stopping the duplication at the source is cleanest.
                var appSettings = new AppSettings
                {
                    SupportedExtensions = Array.Empty<string>(),
                    ExcludedFolderNames = Array.Empty<string>(),
                    ExcludedFileNamePatterns = Array.Empty<string>()
                };
                ctx.Configuration.GetSection("AppSettings").Bind(appSettings);

                // Fall back to constructor defaults if appsettings.json didn't
                // provide any values for these lists.
                var defaults = new AppSettings();
                if (appSettings.SupportedExtensions.Count == 0)
                    appSettings.SupportedExtensions = defaults.SupportedExtensions;
                if (appSettings.ExcludedFolderNames.Count == 0)
                    appSettings.ExcludedFolderNames = defaults.ExcludedFolderNames;
                if (appSettings.ExcludedFileNamePatterns.Count == 0)
                    appSettings.ExcludedFileNamePatterns = defaults.ExcludedFileNamePatterns;

                var store = new JsonSettingsStore(appSettings);

                services.AddSingleton<ISettingsStore>(store);
                services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Current);

                services.AddDbContextFactory<VideoArchiveDbContext>(options =>
                {
                    var dbPath = store.Current.EffectiveDatabasePath;
                    var dir = Path.GetDirectoryName(dbPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    // Apply any pending catalog restore BEFORE SQLite opens
                    // the file. Safe no-op when no .restore-pending sibling
                    // is present.
                    CatalogBackupService.ApplyPendingRestoreIfAny(dbPath);

                    options.UseSqlite($"Data Source={dbPath}");
                });

                services.AddSingleton<IFileSystemService, FileSystemService>();
                services.AddSingleton<IFfprobeService, FfprobeService>();
                services.AddSingleton<IThumbnailService, ThumbnailService>();
                services.AddSingleton<IDjiSrtTelemetryReader, DjiSrtTelemetryReader>();
                services.AddSingleton<ITagService, TagService>();
                services.AddSingleton<ISearchService, SearchService>();
                services.AddSingleton<ICatalogStatisticsService, CatalogStatisticsService>();
                services.AddSingleton<ISavedSearchService, SavedSearchService>();
                services.AddSingleton<IVideoScannerService, VideoScannerService>();
                services.AddSingleton<IVideoLibraryService, VideoLibraryService>();
                services.AddSingleton<ICatalogBackupService, CatalogBackupService>();
                services.AddSingleton<ISidecarService, JsonSidecarService>();
                services.AddSingleton<IProxyResolver, DaVinciProxyResolver>();
                services.AddSingleton<IUpdateService, VelopackUpdateService>();

                // Reverse-geocoding via OpenStreetMap Nominatim. The User-Agent
                // is mandatory per their usage policy. We register the service
                // as a singleton so the per-instance rate limiter remains
                // effective across calls; the HttpClient is created on demand
                // via IHttpClientFactory so socket pooling + DNS refresh still
                // benefit us.
                services.AddHttpClient(NominatimReverseGeocodingService.HttpClientName, client =>
                {
                    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "VideoArchiveManager/0.1.0 (https://github.com/liknes/FindThatShot)");
                    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en;q=0.9, *;q=0.5");
                    client.Timeout = TimeSpan.FromSeconds(15);
                });
                services.AddSingleton<IReverseGeocodingService>(sp =>
                {
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var dbFactory = sp.GetRequiredService<IDbContextFactory<VideoArchiveDbContext>>();
                    var logger = sp.GetService<ILogger<NominatimReverseGeocodingService>>();
                    return new NominatimReverseGeocodingService(
                        () => httpFactory.CreateClient(NominatimReverseGeocodingService.HttpClientName),
                        dbFactory,
                        logger);
                });
                services.AddSingleton<IVideoLocationService, VideoLocationService>();

                services.AddTransient<MainViewModel>();
                services.AddTransient<VideoDetailViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<BulkEditViewModel>();
                services.AddTransient<CatalogStatsViewModel>();

                services.AddTransient<MainWindow>();
                services.AddTransient<DiagnosticsWindow>();
                services.AddTransient<CatalogStatsWindow>();

                // In-app diagnostics buffer + rolling file log. A singleton so
                // the buffer survives across Diagnostics window open/close, and
                // wired into the logging pipeline as its own provider so every
                // ILogger call across the app/services is captured for support.
                services.AddSingleton<Services.Diagnostics.IDiagnosticsLog, Services.Diagnostics.DiagnosticsLog>();

                services.AddLogging(b =>
                {
                    b.AddDebug();
                    b.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sp =>
                        new Services.Diagnostics.DiagnosticsLoggerProvider(
                            sp.GetRequiredService<Services.Diagnostics.IDiagnosticsLog>()));
                    b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

                    // EF Core logs every executed SQL statement at Information,
                    // which floods the diagnostics buffer/file and buries the
                    // app-level events that actually help troubleshooting. Lift
                    // its command category to Warning so the log stays signal.
                    b.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",
                        Microsoft.Extensions.Logging.LogLevel.Warning);
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
                "Find That Shot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _ = RunAutoBackupAsync();

        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        if (splash is not null)
        {
            // Hand off from splash to main window: dismiss the splash with a
            // ~400ms fade once MainWindow.Loaded fires. Done as a single-shot
            // subscription so the splash doesn't linger or get re-dismissed
            // on later layout passes.
            void OnLoaded(object? sender, RoutedEventArgs args)
            {
                mainWindow.Loaded -= OnLoaded;
                splash.FadeOutAndClose();
            }
            mainWindow.Loaded += OnLoaded;
        }

        mainWindow.Show();
    }

    private static async Task RunAutoBackupAsync()
    {
        try
        {
            if (Host is null) return;
            var settings = Host.Services.GetRequiredService<ISettingsStore>().Current;
            if (!settings.AutoBackupOnStartup) return;

            var backup = Host.Services.GetRequiredService<ICatalogBackupService>();
            await backup.BackupNowAsync().ConfigureAwait(false);
        }
        catch
        {
            // Backup failures must never bring the app down; the service
            // already logs the underlying exception.
        }
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
