using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.Persistence.Repositories;

internal sealed class EfUserRepository : IUserRepository
{
    private readonly SalesDbContext _context;

    public EfUserRepository(SalesDbContext context) => _context = context;

    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return _context.Users.FirstOrDefaultAsync(user => user.Username == normalized, ct);
    }

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _context.Users.AddAsync(user, ct);
}
