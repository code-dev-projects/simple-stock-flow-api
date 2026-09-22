namespace SimpleStockFlow.Application.Ports.Outbound;

public interface IPasswordHasher
{
    string Hash(string plainPassword);
    bool Verify(string plainPassword, string storedHash);

    /// <summary>
    /// A well-formed hash that no password matches. Verifying against it costs the same as
    /// checking a real one, so an absent user cannot be told from a wrong password by timing
    /// alone (CA-07.2). Without it the refusal is identical in wording and obvious on a clock.
    /// </summary>
    string DecoyHash { get; }
}
