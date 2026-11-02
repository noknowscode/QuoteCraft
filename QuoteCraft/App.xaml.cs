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
                // Light, Dark or the system's theme (the default); the choice is saved and restored at launch.
                .UseThemeSwitching()
            );
        MainWindow = builder.Window;

#if DEBUG && !NO_UNO_STUDIO
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        // The database, photos and flag files are read as soon as the shell loads; on WebAssembly the browser file
        // system has to be restored first.
        await AppStorage.EnsureReadyAsync();

#if DEBUG
        if (VerificationArgs.Get("seed") == "sample")
        {
            // Evidence runs: sample data goes in before the first page loads.
            try
            {
                await SampleData.SeedAsync();
            }
            catch (Exception ex)
            {
                VerificationArgs.Log($"seed failed: {ex}");
            }
        }
#endif
        Host = await builder.NavigateAsync<Shell>();

        await ShowOnboardingOnFirstRunAsync();
#if DEBUG
        await ApplyVerificationViewAsync();
#endif
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

#if DEBUG
    /// <summary>
    /// Screenshot-matrix hooks (docs/evidence). <c>--qc-theme=light|dark</c> sets the theme through the theme service, as
    /// the app's own selector does. <c>--qc-route=&lt;route&gt;[:detail]</c> opens Dashboard (the quotes list), Clients,
    /// Catalog, Settings, Onboarding or QuoteEditor (the seeded quote with the most line items, opened from the
    /// dashboard like a tap does); <c>:detail</c> also selects the first quote, client or category. Each step prints a
    /// <c>VERIFY</c> line to stderr.
    /// </summary>
    private async Task ApplyVerificationViewAsync()
    {
        try
        {
            if (Host is null || MainWindow?.Content is not FrameworkElement root)
            {
                return;
            }

            if (VerificationArgs.Get("theme") is { } theme)
            {
                var dark = theme.Equals("dark", StringComparison.OrdinalIgnoreCase);
                // The window's theme service, as the shell's theme menu uses it.
                var scope = (FrameworkElement?)await WaitForAsync<MainPage>(root) ?? root;
                var service = scope.FindServiceProvider()?.GetService<Uno.Extensions.Toolkit.IThemeService>();
                if (service is not null)
                {
                    await service.SetThemeAsync(dark ? Uno.Extensions.Toolkit.AppTheme.Dark : Uno.Extensions.Toolkit.AppTheme.Light);
                }
                VerificationArgs.Log($"theme={(dark ? "Dark" : "Light")} service={service is not null} actual={root.ActualTheme}");
            }

            if (VerificationArgs.Get("route") is not { } spec)
            {
                return;
            }

            var parts = spec.Split(':', 2);
            var route = parts[0];
            var detail = parts.Length == 2 && parts[1] == "detail";
            if (route == "Onboarding")
            {
                var response = await root.Navigator()!.NavigateRouteAsync(this, "Onboarding", qualifier: Qualifiers.ClearBackStack);
                VerificationArgs.Log($"route={spec} success={response?.Success}");
                return;
            }

            var main = await WaitForAsync<MainPage>(root);
            if (main is null)
            {
                VerificationArgs.Log($"route={spec} failed: no main page");
                return;
            }

            if (route is "Clients" or "Catalog" or "Settings")
            {
                var response = await main.Navigator()!.NavigateRouteAsync(main, route);
                VerificationArgs.Log($"route={spec} success={response?.Success}");
            }

            if (route == "QuoteEditor")
            {
                var dashboard = await WaitForAsync<DashboardPage>(root);
                var quotes = await Host.Services.GetRequiredService<IQuoteRepository>().GetAllAsync();
                var quote = quotes.OrderByDescending(q => q.LineItems.Count).FirstOrDefault();
                if (dashboard is null || quote is null)
                {
                    VerificationArgs.Log($"route={spec} failed: dashboard={dashboard is not null} quote={quote is not null}");
                    return;
                }
                var response = await dashboard.Navigator()!.NavigateRouteAsync(dashboard, "QuoteEditor", data: quote);
                VerificationArgs.Log($"route={spec} quote={quote.QuoteNumber} success={response?.Success}");
                return;
            }

            if (detail)
            {
                await Task.Delay(1500);
                var selected = route switch
                {
                    "Dashboard" => await SelectFirstAsync<DashboardPage, DashboardModel>(root, async m =>
                        {
                            var all = await Host.Services.GetRequiredService<IQuoteRepository>().GetAllAsync();
                            var first = all.OrderByDescending(q => q.LineItems.Count).First();
                            await m.OpenQuote(first, CancellationToken.None);
                            return first.Title;
                        }),
                    "Clients" => await SelectFirstAsync<ClientsPage, ClientsModel>(root, async m =>
                        {
                            var first = (await m.Clients).First();
                            await m.SelectClient(first, CancellationToken.None);
                            return first.Name;
                        }),
                    "Catalog" => await SelectFirstAsync<CatalogPage, CatalogModel>(root, async m =>
                        {
                            var first = (await m.CategoryCards).First();
                            await m.SelectCategoryCard(first, CancellationToken.None);
                            return first.Name;
                        }),
                    _ => null,
                };
                VerificationArgs.Log($"route={spec} selected={selected}");
            }
        }
        catch (Exception ex)
        {
            VerificationArgs.Log($"failed: {ex}");
        }
    }

    private static async Task<string?> SelectFirstAsync<TPage, TModel>(FrameworkElement root, Func<TModel, Task<string>> select)
        where TPage : FrameworkElement
        where TModel : class
    {
        var page = await WaitForAsync<TPage>(root);
        return page is not null && MvuxHelper.GetModel<TModel>(page.DataContext) is { } model ? await select(model) : null;
    }

    private static async Task<T?> WaitForAsync<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < 100; i++)
        {
            if (Find<T>(root) is { } found)
            {
                return found;
            }
            await Task.Delay(100);
        }
        return null;
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (child is not UIElement element || element.Visibility == Visibility.Visible))
            {
                return match;
            }
            if (Find<T>(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }
#endif

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
