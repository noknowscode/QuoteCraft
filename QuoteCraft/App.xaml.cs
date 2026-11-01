using System.Diagnostics.CodeAnalysis;
using Uno.Resizetizer;

namespace QuoteCraft;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        Uno.UI.FeatureConfiguration.Font.DefaultTextFontFamily = "ms-appx:///Assets/Fonts/DMSans-Regular.ttf#DM Sans";
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    /// <summary>Service provider for pages that need to resolve services directly.</summary>
    public static IServiceProvider Services =>
        ((App)Current).Host!.Services;

    public static Window MainAppWindow => ((App)Current).MainWindow!;

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
        var builder = this.CreateBuilder(args)
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);

                }, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                        .Section<Data.Remote.SupabaseOptions>("Supabase")
                )
                .UseHttp((context, services) => {
#if DEBUG
                // DelegatingHandler will be automatically injected
                services.AddTransient<DelegatingHandler, DebugHttpHandler>();
#endif

})
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<AppDatabase>();
                    services.AddSingleton<IQuoteRepository, QuoteRepository>();
                    services.AddSingleton<IClientRepository, ClientRepository>();
                    services.AddSingleton<IBusinessProfileRepository, BusinessProfileRepository>();
                    services.AddSingleton<ICatalogItemRepository, CatalogItemRepository>();
                    services.AddSingleton<QuoteNumberGenerator>();
#if QUESTPDF
                    services.AddSingleton<Services.IPdfGenerator, Services.PdfGenerator>();
#else
                    services.AddSingleton<Services.IPdfGenerator, Services.UnsupportedPdfGenerator>();
#endif
                    services.AddSingleton<Services.IShareService, Services.ShareService>();
                    services.AddSingleton<Services.IFeatureGateService, Services.FeatureGateService>();
                    services.AddSingleton<Services.IEmailLauncherService, Services.EmailLauncherService>();
                    services.AddSingleton<Services.IOnboardingService, Services.OnboardingService>();
                    services.AddSingleton<Services.IPhotoService, Services.PhotoService>();

                    services.AddSingleton<IStatusHistoryRepository, StatusHistoryRepository>();

                    // Shared HttpClient for auth, sync, and cloud services
                    services.AddSingleton<HttpClient>();

                    // Auth, sync, and cloud services. Local-first: they stay idle and hidden unless
                    // appsettings.json (or QUOTECRAFT_SUPABASE_URL / _ANON_KEY) names a Supabase project.
                    services.AddSingleton<Data.Remote.SupabaseConfig>();
                    services.AddSingleton<Services.IAuthService, Services.AuthService>();
                    services.AddSingleton<Data.Remote.SupabaseClient>();
                    services.AddSingleton<Services.ISyncService, Services.SyncService>();
                    services.AddSingleton<Services.ISubscriptionService, Services.SubscriptionService>();
                    services.AddSingleton<Services.INotificationService, Services.NotificationService>();
                    services.AddSingleton<Helpers.ConnectivityHelper>();
                    services.AddSingleton<Services.QuoteExpiryService>();
                })
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)
            );
        MainWindow = builder.Window;

#if DEBUG && !NO_UNO_STUDIO
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        // The database, photos and flag files are read as soon as the shell loads; on WebAssembly the browser file
        // system has to be restored first.
        await AppStorage.EnsureReadyAsync();

        Host = await builder.NavigateAsync<Shell>();

        await ShowOnboardingOnFirstRunAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App initialization failed: {ex}");
#if DEBUG
            throw;
#endif
        }
    }

    /// <summary>
    /// On first run, onboarding replaces Main; it navigates to Main when it is done. This runs once the first
    /// navigation has finished: a check in ShellModel's constructor raced the default route and never showed, and
    /// one started while Main was still loading left the WebAssembly splash screen up for good.
    /// </summary>
    private async Task ShowOnboardingOnFirstRunAsync()
    {
        var services = Host!.Services;
        if (await services.GetRequiredService<Services.IOnboardingService>().IsOnboardingCompleteAsync())
        {
            return;
        }

        if (MainWindow?.Content is IContentControlProvider { ContentControl: { } root } && root.Navigator() is { } navigator)
        {
            await navigator.NavigateRouteAsync(this, "Onboarding", qualifier: Qualifiers.ClearBackStack);
        }
        else
        {
            services.GetRequiredService<ILogger<App>>().LogError("No navigator to open onboarding");
        }
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellModel)),
            new ViewMap<OnboardingPage, OnboardingModel>(),
            new ViewMap<AuthPage, AuthModel>(),
            new ViewMap<MainPage, MainModel>(),
            new ViewMap<DashboardPage, DashboardModel>(),
            new ViewMap<ClientsPage, ClientsModel>(),
            new ViewMap<CatalogPage, CatalogModel>(),
            new ViewMap<SettingsPage, SettingsModel>(),
            // NotificationsPage is now an overlay panel in MainPage (not a routed page)
            new DataViewMap<ClientEditorPage, ClientEditorModel, ClientEntity>(),
            new DataViewMap<QuoteEditorPage, QuoteEditorModel, QuoteEntity>(),
            new DataViewMap<CatalogEditorPage, CatalogEditorModel, CatalogItemEntity>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellModel>(),
                Nested:
                [
                    new ("Onboarding", View: views.FindByViewModel<OnboardingModel>()),
                    new ("Auth", View: views.FindByViewModel<AuthModel>()),
                    new ("Main", View: views.FindByViewModel<MainModel>(), IsDefault: true,
                        Nested:
                        [
                            new ("Dashboard", View: views.FindByViewModel<DashboardModel>(), IsDefault: true),
                            new ("Clients", View: views.FindByViewModel<ClientsModel>()),
                            new ("Catalog", View: views.FindByViewModel<CatalogModel>()),
                            new ("Settings", View: views.FindByViewModel<SettingsModel>()),
                            new ("ClientEditor", View: views.FindByViewModel<ClientEditorModel>()),
                            new ("QuoteEditor", View: views.FindByViewModel<QuoteEditorModel>()),
                            new ("CatalogEditor", View: views.FindByViewModel<CatalogEditorModel>()),
                        ]),
                ]
            )
        );
    }
}
