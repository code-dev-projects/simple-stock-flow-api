namespace SimpleStockFlow.Application.Ports.Inbound;

public interface IPlaceSale
{
    Task<Guid> HandleAsync(PlaceSaleCommand command, CancellationToken ct = default);
}

/// <param name="SoldByUserId">Read from the token's subject, never from the body (T-12).</param>
/// <param name="SoldByUsername">Frozen on the sale as it was spelled at that moment.</param>
public sealed record PlaceSaleCommand(
    Guid SoldByUserId,
    string SoldByUsername,
    IReadOnlyList<SaleLine> Lines);

public sealed record SaleLine(Guid ProductId, int Quantity);
