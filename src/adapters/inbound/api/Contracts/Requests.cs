using System.ComponentModel.DataAnnotations;

namespace SimpleStockFlow.Adapters.Rest.Contracts;

// HTTP contracts live in the adapter, never in application: they translate JSON into port commands.

/// <summary>
/// The widths the engine actually holds. Declared here because a field wider than its column used
/// to travel all the way down and come back as a 500 with an empty body: the person who typed a
/// long name met a blank failure, and every document of this project says no endpoint answers 500.
/// </summary>
internal static class ColumnWidths
{
    public const int ProductName = 200;
}

/// <summary>A complete product. There is no partial form: E-06 replaces, it does not patch.</summary>
/// <param name="Name">Not empty and not only spaces; the domain trims it.</param>
/// <param name="Price">Strictly greater than zero, two decimals. The currency is not accepted: the system is single-currency.</param>
/// <param name="Stock">Whole and not negative. A decimal is a 400, not a 422.</param>
/// <param name="CategoryId">Must name a category that exists.</param>
public sealed record CreateProductRequest(
    [MaxLength(ColumnWidths.ProductName)] string Name,
    decimal Price,
    int Stock,
    Guid CategoryId);

/// <summary>
/// The replacement for an existing product. Identical to <see cref="CreateProductRequest"/>,
/// and deliberately without an identifier: the one in the route decides which product is written.
/// </summary>
/// <param name="Name">Not empty and not only spaces; the domain trims it.</param>
/// <param name="Price">Strictly greater than zero, two decimals.</param>
/// <param name="Stock">Whole and not negative.</param>
/// <param name="CategoryId">Must name a category that exists.</param>
public sealed record UpdateProductRequest(
    [MaxLength(ColumnWidths.ProductName)] string Name,
    decimal Price,
    int Stock,
    Guid CategoryId);

/// <summary>One line of a sale.</summary>
/// <param name="ProductId">The product must exist and still be active.</param>
/// <param name="Quantity">Whole and greater than zero.</param>
public sealed record SaleLineRequest(Guid ProductId, int Quantity);

/// <summary>
/// A sale. The seller is not part of it: it is read from the token, so sending a name here
/// changes nothing.
/// </summary>
/// <param name="Lines">At least one line, and no product repeated across two of them.</param>
public sealed record PlaceSaleRequest(IReadOnlyList<SaleLineRequest> Lines);

/// <summary>Credentials for the only anonymous operation of the API.</summary>
/// <param name="Username">Trimmed and lowercased before it is looked up, so "  ADMIN  " signs in as "admin".</param>
/// <param name="Password">Never logged and never stored in the clear.</param>
public sealed record LoginRequest(string Username, string Password);

/// <summary>A new operator of the system. Only an administrator may create one.</summary>
/// <param name="Username">Normalised like the login one, and unique.</param>
/// <param name="Password">Hashed by the adapter; the domain never sees it.</param>
/// <param name="Role">Either "admin" or "seller". Anything else is refused by the domain.</param>
public sealed record RegisterRequest(string Username, string Password, string Role);
