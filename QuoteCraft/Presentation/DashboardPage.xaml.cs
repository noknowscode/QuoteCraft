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

        if (clickedChip.Content is string { Length: > 0 } filter && Model is { } model)
        {
            await ErrorReporter.RunAsync(this, "The quotes could not be filtered.",
                async () => await model.SelectedFilter.UpdateAsync(_ => filter, CancellationToken.None));
        }
    }

    // Overdue quotes expire as the list opens (the expiry service also checks every few minutes).
    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunQuietlyAsync("Expiring overdue quotes", async () =>
        {
            if (Model is { } model)
                await model.ExpireOverdueQuotes(CancellationToken.None);
        });

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

    private async void EditQuote_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The quote could not be opened for editing.", async () =>
        {
            if (Model is { } model)
                EditingNotes = await model.GetSelectedQuoteNotesAsync();
            IsEditMode = true;
        });

    private async void DoneEditing_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "Your notes could not be saved.", async () =>
        {
            if (Model is { } model)
                await model.SaveInlineNotes(EditingNotes, CancellationToken.None);
            IsEditMode = false;
        });

    private async void DeleteQuote_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The quote could not be deleted.", async () =>
        {
            var dialog = DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE74D",
                "Delete Quote?",
                "This quote and all its line items will be permanently removed.",
                closeButtonText: "Cancel",
                primaryButtonText: "Delete");

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && Model is { } model)
                await model.DeleteQuote(CancellationToken.None);
        });

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

    private async void InlineAddLineItem_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The line item could not be saved.", async () =>
        {
            var dialog = new LineItemEditorDialog { XamlRoot = this.XamlRoot };
            dialog.SetAddMode();

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } item && Model is { } model)
                await model.SaveInlineLineItem(item, CancellationToken.None);
        });

    private async void InlineLineItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!IsEditMode || sender is not FrameworkElement { DataContext: LineItemEntity item }) return;

        await ErrorReporter.RunAsync(this, "The line item could not be saved.", async () =>
        {
            var dialog = new LineItemEditorDialog { XamlRoot = this.XamlRoot };
            dialog.SetEditMode(item);
            var result = await dialog.ShowAsync();

            if (Model is not { } model) return;
            if (result == ContentDialogResult.Primary && dialog.Result is { } edited)
                await model.SaveInlineLineItem(edited, CancellationToken.None);
            else if (result == ContentDialogResult.Secondary && dialog.WasDeleted)
                await model.DeleteInlineLineItem(dialog.EditingItemId ?? string.Empty, CancellationToken.None);
        });
    }

    // -- Create Quote Dialog ----------------------------------------------------

    private async void CreateQuote_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The quote could not be created.", CreateQuoteAsync);

    private async Task CreateQuoteAsync()
    {
        if (Model is not { } model) return;

        // Feature gate (only where plans exist)
        if (await model.GetQuoteLimitMessageAsync() is { } limit)
        {
            var limitDialog = DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE1D0",
                "Quote Limit Reached",
                limit,
                closeButtonText: "OK",
                primaryButtonText: "Upgrade");
            if (await limitDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                // Plans and the upgrade live in Settings.
                await model.OpenSettings(CancellationToken.None);
            }
            return;
        }

        var titleBox = new TextBox
        {
            Header = "Quote Title",
            Text = string.Empty,
            PlaceholderText = "e.g. Kitchen Renovation",
        };

        var clientNames = await model.GetClientNamesAsync();
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

        var dialog = DialogHelper.BuildBannerDialogWithContent(
            this.XamlRoot,
            "\uE1D0",
            "New Quote",
            "Create a new quote for a client",
            fieldsPanel,
            primaryButtonText: "Create",
            closeButtonText: "Cancel");

        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                DialogHelper.ShowValidation(validation, "Enter a title for the quote.");
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await model.CreateQuoteAsync(titleBox.Text, clientBox.Text);
        }
    }

    // -- Send Quote via Email ---------------------------------------------------

    private async void SendQuote_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The quote could not be sent.", SendQuoteAsync);

    private async Task SendQuoteAsync()
    {
        if (Model is not { } model || await model.GetSendDetailsAsync() is not { } quote) return;

        // Build email fields
        var recipientBox = new TextBox
        {
            Header = "Recipient Email",
            Text = quote.ClientEmail,
            PlaceholderText = "client@example.com",
        };
        var subjectBox = new TextBox
        {
            Header = "Subject",
            Text = $"Quote {quote.QuoteNumber} - {quote.Title}",
        };
        var bodyBox = new TextBox
        {
            Header = "Message",
            Text = $"Hi {quote.ClientName ?? "there"},\n\nPlease find attached quote {quote.QuoteNumber} for \"{quote.Title}\".\n\nThe quote is valid until {quote.ValidUntil?.ToString("MMMM d, yyyy") ?? "further notice"}.\n\nPlease don't hesitate to reach out with any questions.\n\nBest regards,\n{quote.BusinessName}",
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
                        Text = quote.CanAttachPdf
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
            $"Send {quote.QuoteNumber} to {quote.ClientName ?? "client"}",
            fieldsPanel,
            primaryButtonText: "Send via Email",
            closeButtonText: "Cancel");

        if (quote.CanAttachPdf)
        {
            dialog.SecondaryButtonText = "Download PDF Only";
        }

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // The PDF (where this head can make one), then the email, then the quote is marked Sent.
            await model.SendByEmailAsync(
                quote.QuoteId,
                recipientBox.Text?.Trim() ?? string.Empty,
                subjectBox.Text?.Trim() ?? string.Empty,
                bodyBox.Text?.Trim() ?? string.Empty);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await model.ExportPdfAsync(quote.QuoteId);
        }
    }

    // -- Inline Catalog Browser -------------------------------------------------

    private async void InlineCatalogBrowser_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The catalog could not be opened.", async () =>
        {
            if (Model is not { } model || await model.GetCatalogItemsAsync() is not { Count: > 0 } items) return;

            var dialog = new CatalogBrowserDialog { XamlRoot = this.XamlRoot };
            dialog.LoadItems(items);
            // The browser stays open while items are added, so a failure is logged (a second dialog cannot open).
            dialog.ItemAdded += async catalogItem => await ErrorReporter.RunQuietlyAsync("Adding a catalog item",
                async () => await model.AddInlineFromCatalog(catalogItem, CancellationToken.None));
            await dialog.ShowAsync();
        });
}
