using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;

namespace RoomBooking.Tests;

/// <summary>Builds a seeded in-memory database and the service over it.</summary>
public static class TestDatabase
{
    public static BookingService NewService(TimeProvider clock)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<BookingDbContext>().UseSqlite(connection).Options;
        using (var db = new BookingDbContext(options))
            db.Database.EnsureCreated();

        return new BookingService(new TestDbContextFactory(options), clock);
    }
}
