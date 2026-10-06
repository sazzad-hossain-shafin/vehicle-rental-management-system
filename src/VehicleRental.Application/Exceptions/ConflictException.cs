namespace VehicleRental.Application.Exceptions;

/// <summary>
/// The request conflicts with the current state: a duplicate ID, a vehicle that is
/// already rented, or a customer ID that is already used by a different name.
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
