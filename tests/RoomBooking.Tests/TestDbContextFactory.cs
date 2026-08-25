using Microsoft.EntityFrameworkCore;
using RoomBooking.Core.Data;

namespace RoomBooking.Tests;

/// <summary>
/// Hands out contexts over one shared SQLite connection. Each call returns a fresh context with an
/// empty change tracker, which is what makes these tests exercise the same behaviour the app gets
/// from <see cref="IDbContextFactory{TContext}"/> rather than a conveniently cached one.
/// </summary>
public sealed class TestDbContextFactory(DbContextOptions<BookingDbContext> options)
    : IDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext() => new(options);
}
