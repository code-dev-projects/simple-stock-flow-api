namespace SimpleStockFlow.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, long Total)
{
    public int TotalPages => Size == 0 ? 0 : (int)Math.Ceiling(Total / (double)Size);

    public static PagedResult<T> Empty(PageRequest request) => new([], request.Page, request.Size, 0);
}
