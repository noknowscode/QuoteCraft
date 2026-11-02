using Microsoft.UI.Xaml.Media.Imaging;

namespace QuoteCraft.Presentation;

public sealed partial class SettingsPage : Page
{
    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private SettingsModel? Model => MvuxHelper.GetModel<SettingsModel>(DataContext);

    public SettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunQuietlyAsync("Showing the logo", async () =>
        {
            // Wait briefly for the model to load, then refresh logo thumbnails
            await Task.Delay(300);
            if (Model is { } model && await model.LogoPath is { Length: > 0 } logoPath && File.Exists(logoPath))
            {
                UpdateLogoThumbnails(logoPath);
            }
        });

    private async void UploadLogo_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The logo could not be added.", async () =>
        {
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

            if (await picker.PickSingleFileAsync() is { } file && Model is { } model)
            {
                UpdateLogoThumbnails(await model.SetLogoAsync(file));
            }
        });

    private void UpdateLogoThumbnails(string filePath)
    {
        try
        {
            var uri = new Uri(filePath);
            var bitmap = new BitmapImage(uri);

            // Settings form thumbnail
            LogoThumbnailImage.Source = bitmap;
            LogoThumbnailBorder.Visibility = Visibility.Visible;
            LogoFileName.Text = Path.GetFileName(filePath);
            LogoFileName.Visibility = Visibility.Visible;

            // Preview quote logo
            var previewBitmap = new BitmapImage(uri);
            PreviewLogoImage.Source = previewBitmap;
            PreviewLogoImage.Visibility = Visibility.Visible;
            PreviewLogoPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // If we can't load the image, just leave the placeholders
        }
    }
}
