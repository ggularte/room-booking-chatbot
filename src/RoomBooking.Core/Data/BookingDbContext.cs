using Microsoft.EntityFrameworkCore;
using RoomBooking.Core.Domain;

namespace RoomBooking.Core.Data;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Room>(room =>
        {
            room.HasKey(r => r.Id);
            room.Property(r => r.Id).HasMaxLength(1);
            room.HasData(SeedData.Rooms);
        });

        model.Entity<AppUser>(user =>
        {
            user.HasKey(u => u.Id);
            user.HasIndex(u => u.Username).IsUnique();
            user.Property(u => u.Username).HasMaxLength(32);
            user.HasData(SeedData.Users);
        });

        model.Entity<Booking>(booking =>
        {
            booking.HasKey(b => b.Id);
            booking.Property(b => b.Title).HasMaxLength(200);
            booking.HasIndex(b => new { b.RoomId, b.Start });
            booking.HasOne<Room>().WithMany().HasForeignKey(b => b.RoomId).OnDelete(DeleteBehavior.Restrict);
            booking.HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Restrict);
            booking.Ignore(b => b.Duration);
        });
    }
}
