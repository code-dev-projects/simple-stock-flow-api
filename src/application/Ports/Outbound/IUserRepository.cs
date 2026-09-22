using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Application.Ports.Outbound;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}
