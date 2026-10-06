namespace VehicleRental.Application.Exceptions;

/// <summary>
/// A requested entity does not exist (for example, an unknown vehicle ID).
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}
