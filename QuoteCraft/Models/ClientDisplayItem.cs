namespace QuoteCraft.Models;

public partial record ClientDisplayItem(
    string Id,
    string Name,
    string Initials,
    int QuoteCount,
    string City,
    decimal TotalValue,
    ClientEntity Entity);
