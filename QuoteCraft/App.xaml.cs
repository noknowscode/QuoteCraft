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
#if HAS_UNO
        // Uno-only: WinAppSDK has no FeatureConfiguration and takes DM Sans from the theme's DefaultFontFamily alone.
        Uno.UI.FeatureConfiguration.Font.DefaultTextFontFamily = "ms-appx:///Assets/Fonts/DMSans-Regular.ttf#DM Sans";
#endif
        this.InitializeComponent();

        // The safety net under the pages' own error handling: log what nothing else caught.
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorReporter.Log(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorReporter.Log(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    /// <summary>
    /// An exception on the UI thread that no handler caught: logged, and marked handled so one failed action does not
    /// close the app with the user's work in it. Page actions report their own failures (ErrorReporter).
    /// </summary>
    // xaml-lint: allow codebehind - the app-wide exception hook; Application has no XAML or command surface for it
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ErrorReporter.Log(e.Exception, "Unhandled exception on the UI thread");
        e.Handled = true;
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    /// <summary>The services once the host has started; null before (and if startup failed).</summary>
    public static IServiceProvider? ServicesOrNull => (Current as App)?.Host?.Services;

    public static Window MainAppWindow => ((App)Current).MainWindow!;

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
        var builder = this.CreateBuilder(args)
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)
                        .CoreLogLevel(LogLevel.Warning);
                }, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                        .Section<Data.Remote.SupabaseOptions>("Supabase")
                )
                .UseHttp((context, services) =>
                {
#if DEBUG
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
                    services.AddSingleton<Services.QuoteCreationService>();
                    services.AddSingleton<Services.IPdfGenerator, Services.PdfGenerator>();
                    services.AddSingleton<Services.IShareService, Services.ShareService>();
                    services.AddSingleton<Services.IFeatureGateService, Services.FeatureGateService>();
                    services.AddSingleton<Services.IEmailLauncherService, Services.EmailLauncherService>();
                    services.AddSingleton<Services.IOnboardingService, Services.OnboardingService>();
                    services.AddSingleton<Services.IPhotoService, Services.PhotoService>();

                    services.AddSingleton<IStatusHistoryRepository, StatusHistoryRepository>();
                    services.AddSingleton<Services.QuoteStatusService>();

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
                    services.AddSingleton<Services.ThemeChoiceNotifier>();
                })
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)
                // "!" routes to a Page open in this drawer instead of a full-window flyout. After UseNavigation, which
                // registers its own Flyout: the last registration wins.
                .ConfigureServices(services => services.AddTransient<Flyout, DrawerFlyout>())
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

        // Overdue quotes expire, and reminders go out 3 days before (PRD F17), while the app runs.
        Host.Services.GetRequiredService<Services.QuoteExpiryService>().Start();

        await ShowOnboardingOnFirstRunAsync();
#if DEBUG
        await ApplyVerificationViewAsync();
#endif
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "QuoteCraft could not start");
            ShowStartupError(ex);
        }
    }

    /// <summary>
    /// Startup failed (the database could not open, say): say so in the window instead of leaving it blank.
    /// </summary>
    private void ShowStartupError(Exception ex)
    {
        TextBlock Line(string text, string style) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = Resources.TryGetValue(style, out var value) ? value as Style : null,
        };

        var message = new StackPanel
        {
            MaxWidth = Resources.TryGetValue("ReadingMaxWidth", out var width) && width is double max ? max : double.PositiveInfinity,
            Spacing = Resources.TryGetValue("Space12", out var gap) && gap is double space ? space : 0,
            Padding = Resources.TryGetValue("Inset24", out var inset) && inset is Thickness padding ? padding : default,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        message.Children.Add(Line("QuoteCraft could not start", "TitleLarge"));
        message.Children.Add(Line(ex.Message, "BodyMedium"));
        message.Children.Add(Line("Close QuoteCraft and open it again. If this keeps happening, the details are in the app log.", "BodyMedium"));

        MainWindow ??= new Window();
        MainWindow.Content = new Grid
        {
            Background = Resources.TryGetValue("BackgroundBrush", out var brush) ? brush as Microsoft.UI.Xaml.Media.Brush : null,
            Children = { message },
        };
        MainWindow.Activate();
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
    /// dashboard like a tap does); <c>:detail</c> also selects the first quote, client or category, and
    /// <c>Onboarding:step3</c> opens its last step and <c>Onboarding:complete</c> runs it through Create First Quote. Each step prints a
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
                    Host.Services.GetRequiredService<Services.ThemeChoiceNotifier>().Raise();
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
                // The shell's navigator, as ShowOnboardingOnFirstRunAsync uses it.
                var shellNavigator = (MainWindow.Content as IContentControlProvider)?.ContentControl.Navigator();
                var response = shellNavigator is null ? null
                    : await shellNavigator.NavigateRouteAsync(this, "Onboarding", qualifier: Qualifiers.ClearBackStack);
                VerificationArgs.Log($"route={spec} success={response?.Success}");
                // Onboarding:complete walks the steps and presses Create First Quote, as a first-run user would.
                // Onboarding:step3 shows the quote settings step.
                if (parts.Length == 2 && parts[1] is "complete" or "step3"
                    && await WaitForAsync<OnboardingPage>(root) is { } onboarding
                    && onboarding.DataContext is OnboardingViewModel { Model: { } model })
                {
                    await Task.Delay(1000);
                    await model.NextStep(CancellationToken.None);
                    await model.NextStep(CancellationToken.None);
                    if (parts[1] == "complete")
                    {
                        await model.Complete(CancellationToken.None);
                        VerificationArgs.Log($"route={spec} completed editor={await WaitForAsync<QuoteEditorPage>(root) is not null}");
                    }
                }
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
                // Select the tab, as a tap does: navigating the region from here left the window unpainted after a
                // resize under Xvfb (the tab route repaints normally).
                // xaml-lint: allow responsive - not a breakpoint: picks the tab bar that is laid out (Debug-only hook)
                var tab = Descendants<Uno.Toolkit.UI.TabBarItem>(main)
                    .FirstOrDefault(t => Uno.Extensions.Navigation.UI.Region.GetName(t) == route && t.ActualWidth > 0);
                if (tab is not null)
                {
                    tab.IsSelected = true;
                }
                VerificationArgs.Log($"route={spec} tab={tab is not null}");
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
                    "Dashboard" => await SelectFirstAsync<DashboardPage, DashboardModel>(root, vm => (vm as DashboardViewModel)?.Model, async m =>
                        {
                            var all = await Host.Services.GetRequiredService<IQuoteRepository>().GetAllAsync();
                            var first = all.OrderByDescending(q => q.LineItems.Count).First();
                            await m.OpenQuote(new QuoteListItem(first, IsSelected: true), CancellationToken.None);
                            return first.Title;
                        }),
                    "Clients" => await SelectFirstAsync<ClientsPage, ClientsModel>(root, vm => (vm as ClientsViewModel)?.Model, async m =>
                        {
                            var first = (await m.Clients).First();
                            await m.SelectClient(first, CancellationToken.None);
                            return first.Name;
                        }),
                    "Catalog" => await SelectFirstAsync<CatalogPage, CatalogModel>(root, vm => (vm as CatalogViewModel)?.Model, async m =>
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

    /// <summary>Waits for the page, then runs <paramref name="select"/> on its model (the generated view model's Model).</summary>
    private static async Task<string?> SelectFirstAsync<TPage, TModel>(FrameworkElement root, Func<object?, TModel?> model, Func<TModel, Task<string>> select)
        where TPage : FrameworkElement
        where TModel : class
    {
        var page = await WaitForAsync<TPage>(root);
        return page is not null && model(page.DataContext) is { } found ? await select(found) : null;
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

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (child is not UIElement element || element.Visibility is Visibility.Visible))
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
            new ViewMap<NotificationsPage, NotificationsModel>(),
            // Dialogs, opened from models with the "!" qualifier (Helpers/DialogNavigation)
            new DataViewMap<ConfirmDialog, ConfirmModel, ConfirmRequest>(),
            new ResultDataViewMap<NewQuoteDialog, NewQuoteModel, QuoteEntity>(),
            new ResultDataViewMap<SendQuoteDialog, SendQuoteModel, SendQuoteChoice>(Data: new DataMap<QuoteSendDetails>()),
            new ResultDataViewMap<LineItemEditorDialog, LineItemEditorModel, LineItemEditResult>(Data: new DataMap<LineItemEditRequest>()),
            new DataViewMap<CatalogBrowserDialog, CatalogBrowserModel, CatalogBrowserRequest>(),
            new ResultDataViewMap<ClientEditorDialog, ClientEditorModel, ClientEntity>(Data: new DataMap<ClientEditRequest>()),
            new ResultDataViewMap<CatalogItemEditorDialog, CatalogItemEditorModel, CatalogItemEditResult>(Data: new DataMap<CatalogItemEditRequest>()),
            new DataViewMap<QuoteEditorPage, QuoteEditorModel, QuoteEntity>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellModel>(),
                Nested:
                [
                    new ("Onboarding", View: views.FindByViewModel<OnboardingModel>()),
                    new ("Auth", View: views.FindByViewModel<AuthModel>()),
                    new ("Notifications", View: views.FindByViewModel<NotificationsModel>()),
                    new ("Confirm", View: views.FindByViewModel<ConfirmModel>()),
                    new ("NewQuote", View: views.FindByViewModel<NewQuoteModel>()),
                    new ("SendQuote", View: views.FindByViewModel<SendQuoteModel>()),
                    new ("LineItemEditor", View: views.FindByViewModel<LineItemEditorModel>()),
                    new ("CatalogBrowser", View: views.FindByViewModel<CatalogBrowserModel>()),
                    new ("ClientEditor", View: views.FindByViewModel<ClientEditorModel>()),
                    new ("CatalogItemEditor", View: views.FindByViewModel<CatalogItemEditorModel>()),
                    new ("Main", View: views.FindByViewModel<MainModel>(), IsDefault: true,
                        Nested:
                        [
                            new ("Dashboard", View: views.FindByViewModel<DashboardModel>(), IsDefault: true),
                            new ("Clients", View: views.FindByViewModel<ClientsModel>()),
                            new ("Catalog", View: views.FindByViewModel<CatalogModel>()),
                            new ("Settings", View: views.FindByViewModel<SettingsModel>()),
                            new ("QuoteEditor", View: views.FindByViewModel<QuoteEditorModel>()),
                        ]),
                ]
            )
        );
    }
}
