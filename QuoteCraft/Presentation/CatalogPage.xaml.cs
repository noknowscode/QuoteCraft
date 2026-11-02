using Microsoft.UI.Xaml.Input;

namespace QuoteCraft.Presentation;

public sealed partial class CatalogPage : Page
{
    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private CatalogModel? Model => MvuxHelper.GetModel<CatalogModel>(DataContext);

    public CatalogPage()
    {
        this.InitializeComponent();
    }

    // -- Category Card Selection ------------------------------------------------

    private async void CategoryCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogCategoryCard card } && Model is { } model)
        {
            await ErrorReporter.RunAsync(this, "The category could not be opened.",
                async () => await model.SelectCategoryCard(card, CancellationToken.None));
        }
    }

    // -- Item Editing -----------------------------------------------------------

    private async void CatalogItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogDisplayItem item })
            await EditItemAsync(item);
    }

    private async void CatalogItem_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space
            && sender is FrameworkElement { DataContext: CatalogDisplayItem item })
        {
            e.Handled = true;
            await EditItemAsync(item);
        }
    }

    private Task EditItemAsync(CatalogDisplayItem item) =>
        ErrorReporter.RunAsync(this, "The catalog item could not be opened.", async () =>
        {
            if (Model is { } model && await model.GetItemAsync(item.Id) is { } entity)
                await ShowCatalogEditorDialog(model, entity, isNew: false);
        });

    private async void AddItem_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The catalog item could not be added.", async () =>
        {
            if (Model is { } model)
                await ShowCatalogEditorDialog(model, new CatalogItemEntity(), isNew: true);
        });

    private async void AddItemToCategory_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The catalog item could not be added.", async () =>
        {
            if (Model is not { } model) return;

            // Pre-fill category with the currently selected category
            var entity = new CatalogItemEntity();
            if (await model.SelectedCategory is { Length: > 0 } category)
                entity.Category = category;
            await ShowCatalogEditorDialog(model, entity, isNew: true);
        });

    private async Task ShowCatalogEditorDialog(CatalogModel model, CatalogItemEntity entity, bool isNew = false)
    {
        var descBox = new TextBox
        {
            Header = "Description",
            Text = entity.Description ?? string.Empty,
            PlaceholderText = "Item description",
        };
        var priceBox = new TextBox
        {
            Header = $"Unit Price ({Money.Symbol(CurrencyFormatConverter.CurrencyCode)})",
            Text = isNew ? string.Empty : Money.FormatAmountInput(entity.UnitPrice),
            PlaceholderText = Money.FormatAmountInput(0m),
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
        };

        // Category ComboBox with existing categories
        var categories = await model.GetCategoryNamesAsync();
        var categoryBox = new ComboBox
        {
            Header = "Category",
            IsEditable = true,
            PlaceholderText = "e.g. Plumbing, Electrical, Painting",
            ItemsSource = categories,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Set current category value
        var currentCategory = entity.Category ?? string.Empty;
        if (!string.IsNullOrEmpty(currentCategory))
        {
            if (categories.Contains(currentCategory))
                categoryBox.SelectedItem = currentCategory;
            else
                categoryBox.Text = currentCategory;
        }

        var validation = DialogHelper.BuildValidationText(this);

        var fieldsPanel = new StackPanel { Spacing = (double)Application.Current.Resources["Space16"] };
        fieldsPanel.Children.Add(descBox);
        fieldsPanel.Children.Add(priceBox);
        fieldsPanel.Children.Add(categoryBox);
        fieldsPanel.Children.Add(validation);

        var dialog = DialogHelper.BuildBannerDialogWithContent(
            this.XamlRoot,
            "\uE82D",
            isNew ? "Add Catalog Item" : "Edit Catalog Item",
            isNew ? "Add a new item to your pricing catalog" : "Update item details and pricing",
            fieldsPanel,
            primaryButtonText: "Save",
            closeButtonText: "Cancel");

        // Delete is the dialog's own button: a second dialog cannot open while this one is up.
        if (!isNew)
        {
            dialog.SecondaryButtonText = "Delete Item";
        }

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var desc = descBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(desc))
            {
                DialogHelper.ShowValidation(validation, "Enter a description.");
                args.Cancel = true;
                return;
            }

            var price = 0m;
            if (!string.IsNullOrWhiteSpace(priceBox.Text) && !Money.TryParseAmount(priceBox.Text, out price))
            {
                DialogHelper.ShowValidation(validation,
                    $"Enter the unit price as an amount, for example {Money.FormatAmountInput(125m)}.");
                args.Cancel = true;
                return;
            }

            entity.Description = desc;

            // Read category from ComboBox (selected item or typed text)
            var selectedCategory = categoryBox.SelectedItem as string;
            var typedCategory = categoryBox.Text?.Trim();
            entity.Category = !string.IsNullOrEmpty(selectedCategory) ? selectedCategory
                            : !string.IsNullOrEmpty(typedCategory) ? typedCategory
                            : string.Empty;

            entity.UnitPrice = price;

            // Saving: the dialog waits for the database, and stays open with a message if it fails.
            var deferral = args.GetDeferral();
            try
            {
                await model.SaveItemAsync(entity);
            }
            catch (Exception ex)
            {
                ErrorReporter.Log(ex, "Save catalog item");
                DialogHelper.ShowValidation(validation, "The item could not be saved. Try again.");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary && !isNew)
        {
            var confirmDialog = DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE74D",
                "Delete Catalog Item?",
                "This item will be permanently removed from your catalog.",
                closeButtonText: "Cancel",
                primaryButtonText: "Delete");

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await model.DeleteItemAsync(entity.Id);
            }
        }
    }
}
