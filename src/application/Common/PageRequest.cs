namespace SimpleStockFlow.Application.Common;

// A record's own constants are out of scope in its primary constructor, so the default below
// is the literal and DefaultSize is what the normalisation reads. Keep the two in step.
public sealed record PageRequest(int Page = 1, int Size = 20)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;

    public int Page { get; } = Page < 1 ? 1 : Page;

    /// <summary>
    /// CA-01.5 trims instead of refusing, and "larger than the maximum" is not the same case as
    /// "not asked for": falling back to the default would serve a window nobody asked for while
    /// the caller works out its page count from the number reported back.
    /// </summary>
    public int Size { get; } = Size > MaxSize ? MaxSize : Size < 1 ? DefaultSize : Size;

    public int Skip => (Page - 1) * Size;
}
