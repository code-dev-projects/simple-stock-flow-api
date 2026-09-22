using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Application.Ports.Outbound;

public interface ITokenGenerator
{
    (string Token, DateTimeOffset ExpiresAt) Generate(User user);
}
