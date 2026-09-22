using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Application.UnitTests;

/// <summary>
/// The retry covers the use case as a whole, never the write alone: replaying just the write
/// would reapply a discount computed over a stock that is already stale (ADR-002). These tests
/// pin the number of attempts and, above all, what must NOT be retried.
/// </summary>
public sealed class ConflictRetryPolicyTests
{
    // The backoff is a collaborator precisely so the test can make it free.
    private readonly ConflictRetryPolicy _sut = new(_ => TimeSpan.Zero);

    [Fact]
    public async Task Runs_the_operation_once_when_nothing_conflicts()
    {
        var attempts = 0;

        var result = await _sut.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromResult("done");
        });

        result.Should().Be("done");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Retries_the_whole_operation_until_one_attempt_wins()
    {
        var attempts = 0;

        var result = await _sut.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3
                ? throw new ConcurrencyConflictException("Otra operación modificó el producto.")
                : Task.FromResult("done");
        });

        result.Should().Be("done");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Gives_up_after_three_attempts_and_lets_the_conflict_reach_the_caller()
    {
        var attempts = 0;

        var act = () => _sut.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new ConcurrencyConflictException("Otra operación modificó el producto.");
        });

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        attempts.Should().Be(ConflictRetryPolicy.MaxAttempts);
    }

    /// <summary>
    /// A broken business rule is not a race. Retrying it would run the same rejection three
    /// times over and delay a 422 that the first attempt had already settled.
    /// </summary>
    [Fact]
    public async Task Does_not_retry_a_business_rule_violation()
    {
        var attempts = 0;

        var act = () => _sut.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new DomainException("El stock es insuficiente.");
        });

        await act.Should().ThrowAsync<DomainException>();
        attempts.Should().Be(1);
    }
}
