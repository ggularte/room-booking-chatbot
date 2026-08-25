namespace RoomBooking.Agent;

/// <summary>
/// Who the assistant is acting for. The tools read the user from here rather than accepting it as
/// a parameter, so the model cannot book or cancel on behalf of anyone else by supplying a
/// different id.
/// </summary>
public interface IUserContext
{
    string UserId { get; }
}
