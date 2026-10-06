namespace VehicleRental.Application.Abstractions;

/// <summary>
/// One page of results from a repository, plus how many items match in total.
/// </summary>
public sealed record PageResult<T>(IReadOnlyList<T> Items, long TotalCount);
