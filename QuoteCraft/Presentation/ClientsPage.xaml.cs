using Microsoft.UI.Xaml.Input;

namespace QuoteCraft.Presentation;

public sealed partial class ClientsPage : Page
{
    private IClientRepository? _clientRepo;

    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private ClientsModel? Model => MvuxHelper.GetModel<ClientsModel>(DataContext);

    public ClientsPage()
    {
        this.InitializeComponent();
    }

    private IClientRepository ClientRepo =>
        _clientRepo ??= App.Services.GetRequiredService<IClientRepository>();

    private async void AddClient_Click(object sender, RoutedEventArgs e)
    {
        var featureGate = App.Services.GetRequiredService<IFeatureGateService>();
        if (!await featureGate.CanAddClientAsync())
        {
            var limitDialog = Helpers.DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE77B",
                "Client Limit Reached",
                featureGate.GetUpgradeMessage("clients"),
                closeButtonText: "OK",
                primaryButtonText: "Upgrade");
            if (await limitDialog.ShowAsync() == ContentDialogResult.Primary && this.Navigator() is { } navigator)
            {
                // Plans and the upgrade live in Settings.
                await navigator.NavigateRouteAsync(this, "Settings");
            }
            return;
        }

        await ShowClientEditorDialog(new ClientEntity(), isNew: true);
    }

    private async void EditClient_Click(object sender, RoutedEventArgs e)
    {
        // The selection lives in the model (cards select through the list's command, taps and keys alike).
        if (Model is not { } model || await model.SelectedClient is not { } selected) return;

        // Re-fetch to get latest data
        var fresh = await ClientRepo.GetByIdAsync(selected.Id);
        if (fresh is not null)
            await ShowClientEditorDialog(fresh, isNew: false);
    }

    private async Task ShowClientEditorDialog(ClientEntity entity, bool isNew = false)
    {
        var nameBox = new TextBox
        {
            Header = "Name",
            Text = entity.Name ?? string.Empty,
            PlaceholderText = "Client or business name",
        };
        var emailBox = new TextBox
        {
            Header = "Email",
            Text = entity.Email ?? string.Empty,
            PlaceholderText = "email@example.com",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } },
        };
        var phoneBox = new TextBox
        {
            Header = "Phone",
            Text = entity.Phone ?? string.Empty,
            PlaceholderText = "(555) 123-4567",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.TelephoneNumber) } },
        };
        var addressBox = new TextBox
        {
            Header = "Address",
            Text = entity.Address ?? string.Empty,
            PlaceholderText = "Street, City, State/Province",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = (double)Application.Current.Resources["NotesMinHeight"],
        };

        var validation = DialogHelper.BuildValidationText(this);

        var fieldsPanel = new StackPanel { Spacing = (double)Application.Current.Resources["Space16"] };
        fieldsPanel.Children.Add(nameBox);
        fieldsPanel.Children.Add(emailBox);
        fieldsPanel.Children.Add(phoneBox);
        fieldsPanel.Children.Add(addressBox);
        fieldsPanel.Children.Add(validation);

        var dialog = Helpers.DialogHelper.BuildBannerDialogWithContent(
            this.XamlRoot,
            "\uE77B",
            isNew ? "Add Client" : "Edit Client",
            isNew ? "Add a new client to your contact list" : "Update client details",
            fieldsPanel,
            primaryButtonText: "Save",
            closeButtonText: "Cancel");

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var name = nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                DialogHelper.ShowValidation(validation, "Enter the client's name.");
                args.Cancel = true;
                return;
            }

            var email = emailBox.Text?.Trim() ?? string.Empty;
            if (email.Length > 0 && (!email.Contains('@') || !email.Contains('.')))
            {
                DialogHelper.ShowValidation(validation, "Enter a valid email address, or leave it empty.");
                args.Cancel = true;
                return;
            }

            // Saving: the dialog waits for the database before it closes.
            var deferral = args.GetDeferral();

            entity.Name = name;
            entity.Email = emailBox.Text;
            entity.Phone = phoneBox.Text;
            entity.Address = addressBox.Text;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await ClientRepo.SaveAsync(entity);
            }
            catch (Exception)
            {
                DialogHelper.ShowValidation(validation, "The client could not be saved. Try again.");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            RefreshClients();
        }
    }

    private void RefreshClients()
    {
        var model = Model;
        if (model is not null)
            _ = model.RefreshList(CancellationToken.None);
    }

    private async void DeleteClient_Click(object sender, RoutedEventArgs e)
    {
        var model = Model;
        if (model is null) return;

        var dialog = Helpers.DialogHelper.BuildBannerDialog(
            this.XamlRoot,
            "\uE74D",
            "Delete Client?",
            "This client will be permanently removed from your contact list.",
            closeButtonText: "Cancel",
            primaryButtonText: "Delete");

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await model.DeleteClient(CancellationToken.None);
        }
    }

    private async void NewQuoteForClient_Click(object sender, RoutedEventArgs e)
    {
        var model = Model;
        if (model is not null)
            await model.CreateQuoteForClient(CancellationToken.None);
    }

    private void QuoteRow_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            e.Handled = true;
            QuoteRow_Tapped(sender, null);
        }
    }

    private async void QuoteRow_Tapped(object sender, TappedRoutedEventArgs? e)
    {
        if (sender is FrameworkElement { DataContext: QuoteEntity quote })
        {
            var navigator = this.Navigator() ?? throw new InvalidOperationException("The page has no navigator.");
            await navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote);
        }
    }
}
