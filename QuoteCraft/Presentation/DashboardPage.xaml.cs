using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace QuoteCraft.Presentation;

public sealed partial class DashboardPage : Page
{
    private Border? _selectedQuoteCard;
    private Uno.Toolkit.UI.Chip[] _filterChips = [];

    public static readonly DependencyProperty IsEditModeProperty =
        DependencyProperty.Register(
            nameof(IsEditMode), typeof(bool), typeof(DashboardPage),
            new PropertyMetadata(false));

    public bool IsEditMode
    {
        get => (bool)GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    public static readonly DependencyProperty EditingNotesProperty =
        DependencyProperty.Register(
            nameof(EditingNotes), typeof(string), typeof(DashboardPage),
            new PropertyMetadata(string.Empty));

    public string EditingNotes
    {
        get => (string)GetValue(EditingNotesProperty);
        set => SetValue(EditingNotesProperty, value);
    }

    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private DashboardModel? Model => MvuxHelper.GetModel<DashboardModel>(DataContext);

    public DashboardPage()
    {
        this.InitializeComponent();
        this.Loaded += DashboardPage_Loaded;

        _filterChips = [FilterAll, FilterDraft, FilterSent, FilterViewed, FilterAccepted, FilterDeclined, FilterExpired];
    }

    private async void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Uno.Toolkit.UI.Chip clickedChip) return;

        // Enforce single selection: uncheck all others
        foreach (var chip in _filterChips)
            chip.IsChecked = chip == clickedChip;

        var filter = clickedChip.Content as string;
        if (string.IsNullOrEmpty(filter)) return;

        var model = Model;
        if (model is not null)
            await model.SelectedFilter.UpdateAsync(_ => filter, CancellationToken.None);
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-expire overdue quotes on dashboard load
        var model = Model;
        if (model is not null)
            await model.ExpireOverdueQuotes(CancellationToken.None);
    }

    // -- Quote Card Selection ---------------------------------------------------

    private void QuoteCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border tappedCard)
        {
            SelectCard(tappedCard);
            // Model command is invoked via CommandExtensions.Command on the ItemsRepeater
        }
    }

    // Enter or Space on a focused card: InteractionHelper runs OpenQuote; this marks the card selected.
    private void QuoteCard_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is Border card && e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            SelectCard(card);
        }
    }

    private void SelectCard(Border card)
    {
        IsEditMode = false;
        if (_selectedQuoteCard is not null)
        {
            InteractionHelper.SetIsSelected(_selectedQuoteCard, false);
        }
        InteractionHelper.SetIsSelected(card, true);
        _selectedQuoteCard = card;
    }

    // -- Inline Edit ------------------------------------------------------------

    private async void EditQuote_Click(object sender, RoutedEventArgs e)
    {
        IsEditMode = true;
        var model = Model;
        if (model is not null)
            EditingNotes = await model.GetSelectedQuoteNotesAsync();
    }

    private async void DoneEditing_Click(object sender, RoutedEventArgs e)
    {
        IsEditMode = false;
        var model = Model;
        if (model is not null)
            await model.SaveInlineNotes(EditingNotes, CancellationToken.None);
    }

    private async void DeleteQuote_Click(object sender, RoutedEventArgs e)
    {
        var dialog = DialogHelper.BuildBannerDialog(
            this.XamlRoot,
            "\uE74D",
            "Delete Quote?",
            "This quote and all its line items will be permanently removed.",
            closeButtonText: "Cancel",
            primaryButtonText: "Delete");

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var model = Model;
            if (model is not null)
                await model.DeleteQuote(CancellationToken.None);
        }
    }

    // -- Alternate Row Styling --------------------------------------------------

    // The stripe's colour is a ThemeResource in XAML; only its visibility (odd rows) is set here.
    private void LineItems_ElementPrepared(Microsoft.UI.Xaml.Controls.ItemsRepeater sender, Microsoft.UI.Xaml.Controls.ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is Panel { Children.Count: > 0 } row && row.Children[0] is Border { Name: "Stripe" } stripe)
        {
            stripe.Visibility = args.Index % 2 == 1 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // -- Inline Line Item Dialog ------------------------------------------------

    private async void InlineAddLineItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LineItemEditorDialog { XamlRoot = this.XamlRoot };
        dialog.SetAddMode();
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.Result is not null)
        {
            var model = Model;
            if (model is not null)
                await model.SaveInlineLineItem(dialog.Result, CancellationToken.None);
        }
    }

    private async void InlineLineItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!IsEditMode) return;
        if (sender is FrameworkElement { DataContext: LineItemEntity item })
        {
            var dialog = new LineItemEditorDialog { XamlRoot = this.XamlRoot };
            dialog.SetEditMode(item);
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Result is not null)
            {
                var model = Model;
                if (model is not null)
                    await model.SaveInlineLineItem(dialog.Result, CancellationToken.None);
            }
            else if (result == ContentDialogResult.Secondary && dialog.WasDeleted)
            {
                var model = Model;
                if (model is not null)
                    await model.DeleteInlineLineItem(dialog.EditingItemId ?? string.Empty, CancellationToken.None);
            }
        }
    }

    // -- Create Quote Dialog ----------------------------------------------------

    private async void CreateQuote_Click(object sender, RoutedEventArgs e)
    {
        // Feature gate check
        var featureGate = App.Services.GetRequiredService<Services.IFeatureGateService>();
        if (!await featureGate.CanCreateQuoteAsync())
        {
            var limitDialog = Helpers.DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE1D0",
                "Quote Limit Reached",
                featureGate.GetUpgradeMessage("quotes"),
                closeButtonText: "OK",
                primaryButtonText: "Upgrade");
            if (await limitDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                // Plans and the upgrade live in Settings.
                if (this.Navigator() is { } navigator)
                {
                    await navigator.NavigateRouteAsync(this, "Settings");
                }
            }
            return;
        }

        var titleBox = new TextBox
        {
            Header = "Quote Title",
            Text = string.Empty,
            PlaceholderText = "e.g. Kitchen Renovation",
        };

        var clientRepo = App.Services.GetRequiredService<IClientRepository>();
        var allClients = await clientRepo.GetAllAsync();
        var clientNames = allClients.Select(c => c.Name).ToList();

        var clientBox = new AutoSuggestBox
        {
            Header = "Client Name (optional)",
            PlaceholderText = "Start typing a client name...",
            TextBoxStyle = (Style)Application.Current.Resources["MaterialOutlinedTextBoxStyle"],
        };
        clientBox.TextChanged += (s, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = s.Text?.Trim() ?? string.Empty;
                s.ItemsSource = string.IsNullOrEmpty(query)
                    ? null
                    : clientNames.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            }
        };
        clientBox.SuggestionChosen += (s, args) =>
        {
            s.Text = args.SelectedItem as string ?? string.Empty;
        };

        var validation = DialogHelper.BuildValidationText(this);

        var fieldsPanel = new StackPanel { Spacing = (double)Application.Current.Resources["Space16"] };
        fieldsPanel.Children.Add(titleBox);
        fieldsPanel.Children.Add(clientBox);
        fieldsPanel.Children.Add(validation);

        var dialog = Helpers.DialogHelper.BuildBannerDialogWithContent(
            this.XamlRoot,
            "\uE1D0",
            "New Quote",
            "Create a new quote for a client",
            fieldsPanel,
            primaryButtonText: "Create",
            closeButtonText: "Cancel");

        dialog.PrimaryButtonClick += (s, args) =>
        {
            var title = titleBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                DialogHelper.ShowValidation(validation, "Enter a title for the quote.");
                args.Cancel = true;
                return;
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var profileRepo = App.Services.GetRequiredService<IBusinessProfileRepository>();
            var quoteRepo = App.Services.GetRequiredService<IQuoteRepository>();
            var quoteNumGen = App.Services.GetRequiredService<QuoteNumberGenerator>();
            var navigator = this.Navigator() ?? throw new InvalidOperationException("The page has no navigator.");

            var profile = await profileRepo.GetAsync();
            var quoteNumber = await quoteNumGen.GenerateAsync();

            var quote = new QuoteEntity
            {
                Title = titleBox.Text?.Trim() ?? "New Quote",
                QuoteNumber = quoteNumber,
                Status = QuoteStatus.Draft,
                TaxRate = profile.DefaultTaxRate,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(profile.QuoteValidDays),
            };

            // Match client if name was provided
            var clientName = clientBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(clientName))
            {
                quote.ClientName = clientName;
                var match = allClients.FirstOrDefault(c =>
                    c.Name.Equals(clientName, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    quote.ClientId = match.Id;
            }

            await quoteRepo.SaveAsync(quote);
            await navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote);
        }
    }

    // -- Send Quote via Email ---------------------------------------------------

    private async void SendQuote_Click(object sender, RoutedEventArgs e)
    {
        var model = Model;
        if (model is null) return;

        var quote = await model.SelectedQuote;
        if (quote is null) return;

        var quoteRepo = App.Services.GetRequiredService<IQuoteRepository>();
        var freshQuote = await quoteRepo.GetByIdAsync(quote.Id);
        if (freshQuote is null) return;

        // Resolve client email
        var clientEmail = string.Empty;
        if (!string.IsNullOrEmpty(freshQuote.ClientId))
        {
            var clientRepo = App.Services.GetRequiredService<IClientRepository>();
            var client = await clientRepo.GetByIdAsync(freshQuote.ClientId);
            clientEmail = client?.Email ?? string.Empty;
        }

        // Resolve business profile for email template
        var profileRepo = App.Services.GetRequiredService<IBusinessProfileRepository>();
        var profile = await profileRepo.GetAsync();
        var businessName = profile.BusinessName ?? "Our Company";

        var shareService = App.Services.GetRequiredService<Services.IShareService>();
        var canExportPdf = shareService.CanExportPdf;

        // Build email fields
        var recipientBox = new TextBox
        {
            Header = "Recipient Email",
            Text = clientEmail,
            PlaceholderText = "client@example.com",
        };
        var subjectBox = new TextBox
        {
            Header = "Subject",
            Text = $"Quote {freshQuote.QuoteNumber} - {freshQuote.Title}",
        };
        var bodyBox = new TextBox
        {
            Header = "Message",
            Text = $"Hi {freshQuote.ClientName ?? "there"},\n\nPlease find attached quote {freshQuote.QuoteNumber} for \"{freshQuote.Title}\".\n\nThe quote is valid until {freshQuote.ValidUntil?.ToString("MMMM d, yyyy") ?? "further notice"}.\n\nPlease don't hesitate to reach out with any questions.\n\nBest regards,\n{businessName}",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = (double)Application.Current.Resources["MessageMinHeight"],
        };
        var infoBanner = new Border
        {
            Background = ThemeResources.GetBrush(this, "TertiaryContainerBrush"),
            CornerRadius = (CornerRadius)Application.Current.Resources["RadiusMd"],
            Padding = (Thickness)Application.Current.Resources["Inset12x8"],
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = (double)Application.Current.Resources["Space8"],
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE946",
                        FontSize = (double)Application.Current.Resources["IconSizeMd"],
                        Foreground = ThemeResources.GetBrush(this, "OnTertiaryContainerBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = canExportPdf
                            ? "The PDF will be downloaded. Please attach it to the email."
                            : $"{Services.UnsupportedPdfGenerator.UnavailableMessage} The email opens without an attachment.",
                        Style = (Style)Application.Current.Resources["BodySmall"],
                        Foreground = ThemeResources.GetBrush(this, "OnTertiaryContainerBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        var fieldsPanel = new StackPanel { Spacing = (double)Application.Current.Resources["Space12"] };
        fieldsPanel.Children.Add(recipientBox);
        fieldsPanel.Children.Add(subjectBox);
        fieldsPanel.Children.Add(bodyBox);
        fieldsPanel.Children.Add(infoBanner);

        var dialog = Helpers.DialogHelper.BuildBannerDialogWithContent(
            this.XamlRoot,
            "\uE724",
            "Send Quote",
            $"Send {freshQuote.QuoteNumber} to {freshQuote.ClientName ?? "client"}",
            fieldsPanel,
            primaryButtonText: "Send via Email",
            closeButtonText: "Cancel");

        if (canExportPdf)
        {
            dialog.SecondaryButtonText = "Download PDF Only";
        }

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // Download PDF (where the head can make one) + launch email client + mark as sent
            if (canExportPdf)
            {
                await shareService.GenerateAndDownloadPdfAsync(freshQuote);
            }

            var emailService = App.Services.GetRequiredService<Services.IEmailLauncherService>();
            await emailService.ComposeEmailAsync(
                recipientBox.Text?.Trim() ?? string.Empty,
                subjectBox.Text?.Trim() ?? string.Empty,
                bodyBox.Text?.Trim() ?? string.Empty);

            await model.MarkSentAsync(freshQuote.Id);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            // Download PDF only
            await shareService.GenerateAndDownloadPdfAsync(freshQuote);
        }
    }

    // -- Inline Catalog Browser -------------------------------------------------

    private async void InlineCatalogBrowser_Click(object sender, RoutedEventArgs e)
    {
        List<CatalogItemEntity>? items = null;
        var model = Model;
        if (model is not null)
            items = await model.GetCatalogItemsAsync();

        if (items is null || items.Count == 0) return;

        var dialog = new CatalogBrowserDialog { XamlRoot = this.XamlRoot };
        dialog.LoadItems(items);
        dialog.ItemAdded += async catalogItem =>
        {
            var m = Model;
            if (m is not null)
                await m.AddInlineFromCatalog(catalogItem, CancellationToken.None);
        };
        await dialog.ShowAsync();
    }
}
