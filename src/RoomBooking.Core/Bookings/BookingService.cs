using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Core.Data;
using RoomBooking.Core.Domain;

namespace RoomBooking.Core.Bookings;

/// <summary>
/// The four operations the assistant exposes as tools. Every write goes through
/// <see cref="BookingRules"/>, so a booking that violates a constraint cannot be stored no matter
/// what the model was persuaded to ask for.
///
/// A context is created per operation rather than injected. Under Blazor Server a scoped service
/// lives as long as the user's circuit, and a context that long-lived serves values from its change
/// tracker — one user would keep seeing a slot as free after another had taken it.
/// </summary>
public sealed class BookingService(IDbContextFactory<BookingDbContext> dbFactory, TimeProvider clock)
{
    public async Task<BookingResult> CreateBookingAsync(
        string roomId, DateTime start, DateTime end, string? title, int attendees, string userId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        try
        {
            // The overlap check and the insert share a transaction so two concurrent requests cannot
            // both observe a free slot and both write. Overlapping ranges cannot be expressed as a
            // unique index, so serialising here is what keeps the no-double-booking rule true.
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
            var sameRoom = await db.Bookings.Where(b => b.RoomId == roomId).ToListAsync(ct);

            var errors = BookingRules.Validate(
                room, title, start, end, attendees, sameRoom, clock.GetLocalNow().DateTime);
            if (errors.Count > 0)
                return BookingResult.Failed(errors);

            var booking = new Booking
            {
                RoomId = roomId,
                UserId = userId,
                Title = title!.Trim(),
                Start = start,
                End = end,
                Attendees = attendees,
            };

            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return BookingResult.Ok(booking);
        }
        catch (DbException)
        {
            // Serialising the check and the insert means a request arriving mid-transaction waits
            // for the lock, and gives up if it waits too long. That is a refusal the caller can act
            // on — try again — not a fault, and it must not surface as an unhandled exception.
            return BookingResult.Failed([BookingError.CouldNotSecureTheSlot]);
        }
    }

    /// <summary>Rooms with nothing booked anywhere in the requested range.</summary>
    public async Task<IReadOnlyList<RoomAvailability>> ListAvailableRoomsAsync(
        DateTime start, DateTime end, int? minimumCapacity = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rooms = await db.Rooms.OrderBy(r => r.Id).ToListAsync(ct);
        var overlapping = await db.Bookings
            .Where(b => start < b.End && b.Start < end)
            .ToListAsync(ct);

        return rooms
            .Where(r => minimumCapacity is null || r.Capacity >= minimumCapacity)
            .Select(r => new RoomAvailability(r.Id, r.Capacity, overlapping.All(b => b.RoomId != r.Id)))
            .ToList();
    }

    /// <summary>Slot-by-slot occupancy for one room, so the assistant can describe real gaps.</summary>
    public async Task<RoomSchedule?> GetRoomScheduleAsync(
        string roomId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null)
            return null;

        // Reads are lenient about alignment: widen to the enclosing slot boundaries rather than
        // rejecting, since asking "what does room A look like from 09:45?" is a reasonable question.
        var windowStart = FloorToSlot(from);
        var windowEnd = CeilingToSlot(to);

        var bookings = await db.Bookings
            .Where(b => b.RoomId == roomId && windowStart < b.End && b.Start < windowEnd)
            .ToListAsync(ct);

        var slots = BookingRules.SlotsIn(windowStart, windowEnd)
            .Select(slotStart =>
            {
                var slotEnd = slotStart.AddMinutes(BookingRules.SlotMinutes);
                var holder = bookings.FirstOrDefault(b => slotStart < b.End && b.Start < slotEnd);
                return new SlotStatus(slotStart, slotEnd, holder is null, holder?.Title, holder?.UserId);
            })
            .ToList();

        return new RoomSchedule(room.Id, room.Capacity, slots);
    }

    /// <summary>Cancels a booking, refusing anything the requesting user does not own.</summary>
    public async Task<CancelResult> CancelBookingAsync(Guid bookingId, string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
            return CancelResult.Failed(CancelError.BookingNotFound);

        if (booking.UserId != userId)
            return CancelResult.Failed(CancelError.NotOwnedByUser);

        db.Bookings.Remove(booking);
        await db.SaveChangesAsync(ct);
        return CancelResult.Ok();
    }

    /// <summary>Bookings held by one user, for the assistant to reference when cancelling.</summary>
    public async Task<IReadOnlyList<Booking>> ListUserBookingsAsync(
        string userId, DateTime? from = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.Bookings.Where(b => b.UserId == userId);
        if (from is not null)
            query = query.Where(b => b.End > from);

        return await query.OrderBy(b => b.Start).ToListAsync(ct);
    }

    private static DateTime FloorToSlot(DateTime moment)
    {
        var ticks = TimeSpan.FromMinutes(BookingRules.SlotMinutes).Ticks;
        return new DateTime(moment.Ticks - moment.Ticks % ticks, moment.Kind);
    }

    private static DateTime CeilingToSlot(DateTime moment)
    {
        var floored = FloorToSlot(moment);
        return floored == moment ? moment : floored.AddMinutes(BookingRules.SlotMinutes);
    }
}
