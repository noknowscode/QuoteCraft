using Microsoft.UI.Xaml.Input;

namespace QuoteCraft.Presentation;

public sealed partial class CatalogPage : Page
{
    private ICatalogItemRepository? _catalogRepo;

    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private CatalogModel? Model => MvuxHelper.GetModel<CatalogModel>(DataContext);

    public CatalogPage()
    {
        this.InitializeComponent();
    }

    private ICatalogItemRepository CatalogRepo =>
        _catalogRepo ??= App.Services.GetRequiredService<ICatalogItemRepository>();

    // -- Category Card Selection ------------------------------------------------

    private async void CategoryCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogCategoryCard card })
        {
            var model = Model;
            if (model is not null)
                await model.SelectCategoryCard(card, CancellationToken.None);
        }
    }

    // -- Item Editing -----------------------------------------------------------

    private async void CatalogItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogDisplayItem item })
        {
            var entity = await CatalogRepo.GetByIdAsync(item.Id);
            if (entity is not null)
                await ShowCatalogEditorDialog(entity, isNew: false);
        }
    }

    private async void CatalogItem_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space
            && sender is FrameworkElement { DataContext: CatalogDisplayItem item })
        {
            e.Handled = true;
            var entity = await CatalogRepo.GetByIdAsync(item.Id);
            if (entity is not null)
                await ShowCatalogEditorDialog(entity, isNew: false);
        }
    }

    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        await ShowCatalogEditorDialog(new CatalogItemEntity(), isNew: true);
    }

    private async void AddItemToCategory_Click(object sender, RoutedEventArgs e)
    {
        // Pre-fill category with the currently selected category
        var entity = new CatalogItemEntity();
        var catalogModel = Model;
        if (catalogModel is not null)
        {
            var category = await catalogModel.SelectedCategory;
            if (!string.IsNullOrEmpty(category))
                entity.Category = category;
        }
        await ShowCatalogEditorDialog(entity, isNew: true);
    }

    private async Task ShowCatalogEditorDialog(CatalogItemEntity entity, bool isNew = false)
    {

        var descBox = new TextBox
        {
            Header = "Description",
            Text = entity.Description ?? string.Empty,
            PlaceholderText = "Item description",
        };
        var priceBox = new TextBox
        {
            Header = "Unit Price ($)",
            Text = isNew ? string.Empty : entity.UnitPrice.ToString("F2"),
            PlaceholderText = "0.00",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
        };

        // Category ComboBox with existing categories
        var categories = await CatalogRepo.GetCategoriesAsync();
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

        var dialog = Helpers.DialogHelper.BuildBannerDialogWithContent(
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

            if (!string.IsNullOrWhiteSpace(priceBox.Text)
                && (!decimal.TryParse(priceBox.Text, out var enteredPrice) || enteredPrice < 0))
            {
                DialogHelper.ShowValidation(validation, "Enter the unit price as a number, for example 125.00.");
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

            var priceStr = priceBox.Text ?? "0";
            if (decimal.TryParse(priceStr, out var price))
                entity.UnitPrice = price;

            await CatalogRepo.SaveAsync(entity);
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            RefreshCatalog();
        }
        else if (result == ContentDialogResult.Secondary && !isNew)
        {
            var confirmDialog = Helpers.DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE74D",
                "Delete Catalog Item?",
                "This item will be permanently removed from your catalog.",
                closeButtonText: "Cancel",
                primaryButtonText: "Delete");

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await CatalogRepo.DeleteAsync(entity.Id);
                RefreshCatalog();
            }
        }
    }

    private void RefreshCatalog()
    {
        var model = Model;
        if (model is not null)
            _ = model.RefreshList(CancellationToken.None);
    }
}
