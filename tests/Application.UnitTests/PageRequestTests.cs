using SimpleStockFlow.Application.Common;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// CA-01.5 and D-C5: a size over the maximum is served AS the maximum, and the response carries
/// the size served rather than the one asked for. Falling back to the default instead makes the
/// client work out the page count from a size it is not getting, and its paginator skips rows.
/// </summary>
public sealed class PageRequestTests
{
    private const int DefaultSize = 20;

    [Fact]
    public void An_absent_size_is_served_as_the_default()
    {
        new PageRequest().Size.Should().Be(DefaultSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_size_below_one_is_served_as_the_default(int size)
    {
        new PageRequest(1, size).Size.Should().Be(DefaultSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(99)]
    [InlineData(100)]
    public void A_size_within_range_is_served_as_asked(int size)
    {
        new PageRequest(1, size).Size.Should().Be(size);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(500)]
    [InlineData(int.MaxValue)]
    public void A_size_over_the_maximum_is_served_as_the_maximum(int size)
    {
        new PageRequest(1, size).Size.Should().Be(PageRequest.MaxSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(int.MinValue)]
    public void A_page_below_one_is_served_as_the_first(int page)
    {
        new PageRequest(page, 20).Page.Should().Be(1);
    }

    [Fact]
    public void A_page_within_range_is_served_as_asked()
    {
        new PageRequest(4, 20).Page.Should().Be(4);
    }

    /// <summary>
    /// The offset is the place where serving one size while reporting another would go wrong in
    /// silence: it would read a window the caller never asked for.
    /// </summary>
    [Fact]
    public void The_offset_follows_the_size_served_and_not_the_one_asked()
    {
        new PageRequest(3, 500).Skip.Should().Be(2 * PageRequest.MaxSize);
    }
}
