using Microsoft.UI.Xaml.Input;

namespace QuoteCraft.Presentation;

public sealed partial class QuoteEditorPage : Page
{
    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private QuoteEditorModel? Model => MvuxHelper.GetModel<QuoteEditorModel>(DataContext);

    public QuoteEditorPage()
    {
        this.InitializeComponent();
    }

    // -- Back Navigation with Unsaved Changes Warning --------------------------

    private async void BackButton_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The editor could not close.", async () =>
        {
            if (Model is not { } model) return;

            if (await model.HasUnsavedChangesAsync())
            {
                var dialog = DialogHelper.BuildBannerDialog(
                    this.XamlRoot,
                    "\uE7BA",
                    "Unsaved Changes",
                    "You have unsaved changes. Are you sure you want to leave?",
                    closeButtonText: "Stay",
                    primaryButtonText: "Discard");
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }

            await model.GoBack(CancellationToken.None);
        });

    // -- Line Item Dialog -------------------------------------------------------

    private async void OpenAddLineItem_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The line item could not be saved.", async () =>
        {
            var dialog = new LineItemEditorDialog { XamlRoot = this.XamlRoot };
            dialog.SetAddMode();

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } item && Model is { } model)
                await model.SaveLineItem(item, CancellationToken.None);
        });

    private void LineItemCard_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space && e.OriginalSource == sender)
        {
            e.Handled = true;
            LineItemCard_Tapped(sender, null);
        }
    }

    private async void LineItemCard_Tapped(object sender, TappedRoutedEventArgs? e)
    {
        if (sender is not FrameworkElement { DataContext: LineItemEntity item }) return;

        await ErrorReporter.RunAsync(this, "The line item could not be saved.", async () =>
        {
            var dialog = new LineItemEditorDialog { XamlRoot = this.XamlRoot };
            dialog.SetEditMode(item);
            var result = await dialog.ShowAsync();

            if (Model is not { } model) return;
            if (result == ContentDialogResult.Primary && dialog.Result is { } edited)
                await model.SaveLineItem(edited, CancellationToken.None);
            else if (result == ContentDialogResult.Secondary && dialog.WasDeleted)
                await model.DeleteLineItem(new LineItemEntity { Id = dialog.EditingItemId ?? string.Empty }, CancellationToken.None);
        });
    }

    // -- Catalog Browser Dialog -------------------------------------------------

    private async void OpenCatalogBrowser_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The catalog could not be opened.", async () =>
        {
            if (Model is not { } model || await model.GetCatalogItemsAsync() is not { Count: > 0 } items) return;

            var dialog = new CatalogBrowserDialog { XamlRoot = this.XamlRoot };
            dialog.LoadItems(items);
            // The browser stays open while items are added, so a failure is logged (a second dialog cannot open).
            dialog.ItemAdded += async catalogItem => await ErrorReporter.RunQuietlyAsync("Adding a catalog item",
                async () => await model.AddFromCatalog(catalogItem, CancellationToken.None));
            await dialog.ShowAsync();
        });

    // -- Photo Picker -----------------------------------------------------------

    private async void AddPhoto_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The photo could not be added.", async () =>
        {
            if (Model is not { } model) return;

            if (model.PhotoLimitReached)
            {
                var limitDialog = DialogHelper.BuildBannerDialog(
                    this.XamlRoot,
                    "\uE722",
                    "Photo Limit Reached",
                    $"You can attach up to {model.MaxPhotos} photos per quote.",
                    closeButtonText: "OK");
                await limitDialog.ShowAsync();
                return;
            }

            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");

#if WINDOWS
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow));
#endif

            if (await picker.PickSingleFileAsync() is { } file)
                await model.AddPhotoFromFile(file, CancellationToken.None);
        });

    // -- Client AutoSuggest -----------------------------------------------------

    private async void ClientAutoSuggest_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var query = sender.Text?.Trim() ?? string.Empty;
        if (query.Length < 1)
        {
            sender.ItemsSource = null;
            return;
        }

        await ErrorReporter.RunQuietlyAsync("Client suggestions", async () =>
        {
            if (Model is { } model)
                sender.ItemsSource = await model.SearchClientNamesAsync(query);
        });
    }

    private void ClientAutoSuggest_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string clientName)
            sender.Text = clientName;
    }
}
