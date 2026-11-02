using System.Globalization;

namespace QuoteCraft.Presentation;

public sealed partial class LineItemEditorDialog : ContentDialog
{
    private string? _editingItemId;

    public LineItemEditorDialog()
    {
        this.InitializeComponent();
        this.PrimaryButtonClick += OnPrimaryButtonClick;
        this.SecondaryButtonClick += OnSecondaryButtonClick;

        // Prices in the business's currency, typed in the user's own number format.
        UnitPriceBox.Header = $"Unit Price ({Money.Symbol(CurrencyFormatConverter.CurrencyCode)})";
        UnitPriceBox.PlaceholderText = Money.FormatAmountInput(0m);
    }

    /// <summary>
    /// The line item after Save: its description, price and quantity, and its id when editing. Its place in the quote
    /// is the repository's business (a new item goes last, an edited one stays put). Null if cancelled.
    /// </summary>
    public LineItemEntity? Result { get; private set; }

    /// <summary>
    /// True if the user clicked Delete in edit mode.
    /// </summary>
    public bool WasDeleted { get; private set; }

    /// <summary>
    /// The ID of the line item being edited, if in edit mode.
    /// </summary>
    public string? EditingItemId => _editingItemId;

    public void SetAddMode()
    {
        _editingItemId = null;
        SecondaryButtonText = "";
        DescriptionBox.Text = string.Empty;
        UnitPriceBox.Text = string.Empty;
        QuantityBox.Text = "1";
        Result = null;
        WasDeleted = false;
        UpdateLineTotal();
    }

    public void SetEditMode(LineItemEntity item)
    {
        _editingItemId = item.Id;
        SecondaryButtonText = "Delete";
        DescriptionBox.Text = item.Description;
        UnitPriceBox.Text = Money.FormatAmountInput(item.UnitPrice);
        QuantityBox.Text = item.Quantity.ToString(CultureInfo.CurrentCulture);
        Result = null;
        WasDeleted = false;
        UpdateLineTotal();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var error = LineItemInput.TryRead(DescriptionBox.Text, UnitPriceBox.Text, QuantityBox.Text,
            out var description, out var unitPrice, out var quantity);
        if (error is not null)
        {
            // Say what is wrong and keep the dialog open, rather than saving a guess.
            ValidationText.Text = error;
            ValidationText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        ValidationText.Visibility = Visibility.Collapsed;
        Result = new LineItemEntity
        {
            Description = description,
            UnitPrice = unitPrice,
            Quantity = quantity,
        };

        if (!string.IsNullOrEmpty(_editingItemId))
            Result.Id = _editingItemId;
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        WasDeleted = true;
    }

    private void MinusQty_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(QuantityBox.Text, out var qty) && qty > 1)
            QuantityBox.Text = (qty - 1).ToString(CultureInfo.CurrentCulture);
        UpdateLineTotal();
    }

    private void PlusQty_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(QuantityBox.Text, out var qty))
            QuantityBox.Text = (qty + 1).ToString(CultureInfo.CurrentCulture);
        else
            QuantityBox.Text = "1";
        UpdateLineTotal();
    }

    private void PriceOrQty_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateLineTotal();
    }

    /// <summary>The running line total, or a dash while the price or quantity does not read.</summary>
    private void UpdateLineTotal()
    {
        var price = 0m;
        var priceReads = string.IsNullOrWhiteSpace(UnitPriceBox.Text) || Money.TryParseAmount(UnitPriceBox.Text, out price);
        var quantityReads = int.TryParse(QuantityBox.Text, NumberStyles.None, CultureInfo.CurrentCulture, out var quantity) && quantity >= 1;
        LineTotalText.Text = priceReads && quantityReads
            ? Money.Format(Money.LineTotal(price, quantity), CurrencyFormatConverter.CurrencyCode)
            : "—";
    }
}
