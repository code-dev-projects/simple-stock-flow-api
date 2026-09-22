using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimpleStockFlow.Application.Common;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Common;

namespace SimpleStockFlow.Adapters.Persistence;

internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly SalesDbContext _context;

    public EfUnitOfWork(SalesDbContext context) => _context = context;

    public void DiscardChanges() => _context.ChangeTracker.Clear();

    /// <summary>
    /// The border where the ORM's concurrency exception stops. Letting DbUpdateConcurrencyException
    /// travel further would put an infrastructure type into the application's catch clauses,
    /// which is the dependency rule of the hexagon broken in one line (ADR-002).
    /// </summary>
    public async Task<int> CommitAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException conflict)
        {
            throw new ConcurrencyConflictException(
                "Otra operación modificó los mismos datos al mismo tiempo. Vuelve a intentarlo.",
                conflict);
        }
        catch (DbUpdateException failure) when (IsUniqueViolation(failure))
        {
            // A checked-then-taken value is a broken rule, not a server fault: the caller asked
            // for something the data no longer allows. Retrying it would fail the same way, so
            // this is a 422 and not the 409 above.
            throw new DomainException(
                "Ya existe un registro con ese valor y debe ser único. Alguien pudo adelantarse.",
                failure);
        }
    }

    /// <summary>
    /// 23505 is the SQLSTATE Postgres raises for a unique violation. Matching on the code and
    /// not on the message keeps this working in any server locale.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException failure) =>
        failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
