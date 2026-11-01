namespace QuoteCraft.Services;

public interface IPdfGenerator
{
    /// <summary>
    /// False on the heads where no PDF engine is available (Android, iOS and WebAssembly);
    /// see docs/DECISIONS-production-readiness.md, D2.
    /// </summary>
    bool IsSupported { get; }

    Task<string> GenerateQuotePdfAsync(QuoteEntity quote);
}

/// <summary>
/// The generator for heads without a PDF engine. The UI hides PDF export there, so a call is a programming error.
/// </summary>
public sealed class UnsupportedPdfGenerator : IPdfGenerator
{
    public const string UnavailableMessage = "PDF export is available in the desktop and Windows apps.";

    public bool IsSupported => false;

    public Task<string> GenerateQuotePdfAsync(QuoteEntity quote) =>
        throw new PlatformNotSupportedException(UnavailableMessage);
}
