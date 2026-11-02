namespace QuoteCraft.Models;

public partial record QuoteDetail(QuoteEntity Quote, IImmutableList<LineItemEntity> LineItems, ClientEntity? Client = null)
{
    /// <summary>Shows the Reopen banner (PRD F17.3).</summary>
    public bool IsExpired => Quote.Status == QuoteStatus.Expired;
}
