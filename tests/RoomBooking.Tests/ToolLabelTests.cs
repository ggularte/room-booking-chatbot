using RoomBooking.Web;

namespace RoomBooking.Tests;

public class ToolLabelTests
{
    [Theory]
    [InlineData("list_my_bookings<|channel|>functions.list_my_bookings", "list_my_bookings")]
    [InlineData("functions.create_booking", "create_booking")]
    [InlineData("create_booking\n", "create_booking")]
    [InlineData("<|start|>", "tool")]
    [InlineData("", "tool")]
    [InlineData(null, "tool")]
    public void Strips_what_the_model_was_saying_to_itself(string? emitted, string expected)
    {
        Assert.Equal(expected, ToolLabel.Display(emitted));
    }

    [Theory]
    [InlineData("create_booking")]
    [InlineData("list_available_rooms")]
    [InlineData("get_room_schedule")]
    public void Leaves_a_well_formed_name_alone(string name)
    {
        Assert.Equal(name, ToolLabel.Display(name));
    }
}
