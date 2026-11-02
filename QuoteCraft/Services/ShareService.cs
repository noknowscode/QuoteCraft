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
}

public class ShareService : IShareService
{
    private readonly IPdfGenerator _pdfGenerator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly ILogger<ShareService> _logger;

    public ShareService(
        IPdfGenerator pdfGenerator,
        IQuoteRepository quoteRepo,
        ILogger<ShareService> logger)
    {
        _pdfGenerator = pdfGenerator;
        _quoteRepo = quoteRepo;
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
