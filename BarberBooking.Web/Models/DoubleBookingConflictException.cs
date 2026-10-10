namespace BarberBooking.Web.Models;

public class DoubleBookingConflictException : Exception
{
    public DoubleBookingConflictException(string message) : base(message)
    {
    }
}
