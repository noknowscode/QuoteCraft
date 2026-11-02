using Microsoft.UI.Xaml.Media;

namespace QuoteCraft.Presentation;

public sealed partial class CatalogBrowserDialog : ContentDialog
{
    private List<CatalogItemEntity> _allItems = [];
    private string _searchText = string.Empty;
    private string _selectedCategory = "All";

    public CatalogBrowserDialog()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Fired when the user clicks the Add button on a catalog item.
    /// </summary>
    public event Action<CatalogItemEntity>? ItemAdded;

    public void LoadItems(List<CatalogItemEntity> items)
    {
        _allItems = items;
        _searchText = string.Empty;
        _selectedCategory = "All";
        SearchBox.Text = string.Empty;
        BuildCategoryChips();
        FilterItems();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text ?? string.Empty;
        FilterItems();
    }

    private void BuildCategoryChips()
    {
        CategoryChips.Children.Clear();
        var categories = new List<string> { "All" };
        categories.AddRange(_allItems.Select(i => i.Category).Distinct().OrderBy(c => c));

        foreach (var cat in categories)
        {
            // Material chips: their checked state and colours come from the theme, in Light and Dark alike.
            var chip = new Uno.Toolkit.UI.Chip
            {
                Content = cat,
                Style = (Style)Application.Current.Resources["MaterialChipStyle"],
                IsChecked = cat == _selectedCategory,
                Tag = cat,
            };
            chip.Click += CategoryChip_Click;
            CategoryChips.Children.Add(chip);
        }
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string cat })
        {
            _selectedCategory = cat;
            foreach (var child in CategoryChips.Children)
            {
                if (child is Uno.Toolkit.UI.Chip chip)
                    chip.IsChecked = chip.Tag as string == _selectedCategory;
            }
            FilterItems();
        }
    }

    private void FilterItems()
    {
        IEnumerable<CatalogItemEntity> filtered = _allItems;

        if (_selectedCategory != "All")
            filtered = filtered.Where(i => i.Category.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_searchText))
            filtered = filtered.Where(i =>
                i.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        CatalogItemsList.ItemsSource = filtered.ToList();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogItemEntity item })
            ItemAdded?.Invoke(item);
    }

}
