#if QUESTPDF
using System.Diagnostics;
#endif

namespace QuoteCraft.Services;

public interface IShareService
{
    /// <summary>False on the heads without a PDF engine (Android, iOS, WebAssembly).</summary>
    bool CanExportPdf { get; }

    /// <summary>Writes the quote's PDF and opens it. It changes nothing on the quote: sending is a separate step.</summary>
    Task GenerateAndDownloadPdfAsync(QuoteEntity quote);
    Task<string> GenerateShareLinkAsync(QuoteEntity quote);
    Task CopyShareLinkAsync(QuoteEntity quote);
    Task ShareViaSmsAsync(QuoteEntity quote, string phoneNumber);
}

public class ShareService : IShareService
{
    private readonly IPdfGenerator _pdfGenerator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly QuoteStatusService _status;
    private readonly Data.Remote.SupabaseConfig _supabase;
    private readonly ILogger<ShareService> _logger;

    public ShareService(
        IPdfGenerator pdfGenerator,
        IQuoteRepository quoteRepo,
        QuoteStatusService status,
        Data.Remote.SupabaseConfig supabase,
        ILogger<ShareService> logger)
    {
        _pdfGenerator = pdfGenerator;
        _quoteRepo = quoteRepo;
        _status = status;
        _supabase = supabase;
        _logger = logger;
    }

    public bool CanExportPdf => _pdfGenerator.IsSupported;

    public async Task GenerateAndDownloadPdfAsync(QuoteEntity quote)
    {
        // Ensure line items are loaded
        if (quote.LineItems.Count == 0)
            quote.LineItems = await _quoteRepo.GetLineItemsAsync(quote.Id);

#if QUESTPDF
        var filePath = await _pdfGenerator.GenerateQuotePdfAsync(quote);

        // Desktop and Windows (the heads with a PDF engine): open the PDF with the system's default application.
        OpenFileWithDefaultApp(filePath);
#else
        // The pages offer no PDF export on this head (CanExportPdf is false).
        throw new PlatformNotSupportedException(UnsupportedPdfGenerator.UnavailableMessage);
#endif
    }

    public async Task<string> GenerateShareLinkAsync(QuoteEntity quote)
    {
        // The client-facing page is a Supabase Edge Function (supabase/functions/quote-view).
        _supabase.EnsureConfigured();

        var fresh = await _quoteRepo.GetByIdAsync(quote.Id) ?? quote;

        // Generate share token if not already present
        if (string.IsNullOrEmpty(fresh.ShareToken))
        {
            fresh.ShareToken = GenerateShareToken();
            fresh.UpdatedAt = DateTimeOffset.UtcNow;
            await _quoteRepo.SaveAsync(fresh);
        }

        // Construct the client-facing URL
        return $"{_supabase.Url}/functions/v1/quote-view?token={fresh.ShareToken}";
    }

    public async Task CopyShareLinkAsync(QuoteEntity quote)
    {
        var link = await GenerateShareLinkAsync(quote);

        // Copy to clipboard using platform-specific approach
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(link);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
    }

    public async Task ShareViaSmsAsync(QuoteEntity quote, string phoneNumber)
    {
        var link = await GenerateShareLinkAsync(quote);
        var message = $"Here's your quote #{quote.QuoteNumber}: {link}";

        // Use the system SMS launcher
        var uri = new Uri($"sms:{phoneNumber}?body={Uri.EscapeDataString(message)}");
        await Windows.System.Launcher.LaunchUriAsync(uri);

        await _status.MarkSentAsync(quote.Id);
    }

    private static string GenerateShareToken()
    {
        var bytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

#if QUESTPDF
    private void OpenFileWithDefaultApp(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shell execute failed for {FilePath}, trying platform fallback", filePath);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", $"\"{filePath}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", filePath);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", filePath);
                }
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "All file open methods failed for {FilePath}", filePath);
            }
        }
    }
#endif
}
