using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// EF Core adds an index for every foreign key by convention. A few Phase 14A-3 foreign keys have no query behind them
/// yet, so those indexes would only slow writes (every insert would add an entry for a column that is always NULL).
/// This replaces the built-in convention with one that skips exactly those keys:
///
///   Reservations.CompanyId, Rentals.CompanyId   NULL until the backfill; tenant indexes come with the contract step
///   CompanyMemberships.CreatedByUserId          nothing looks members up by who added them
///
/// Every other foreign key behaves as before. Indexes declared explicitly are never affected.
/// </summary>
internal sealed class NoUnneededForeignKeyIndexesConvention : ForeignKeyIndexConvention
{
    private static readonly (Type Entity, string Property)[] Unwanted =
    [
        (typeof(Reservation), "CompanyId"),
        (typeof(Rental), "CompanyId"),
        (typeof(CompanyMembership), nameof(CompanyMembership.CreatedByUserId)),
    ];

    public NoUnneededForeignKeyIndexesConvention(ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void ProcessForeignKeyAdded(
        IConventionForeignKeyBuilder foreignKeyBuilder,
        IConventionContext<IConventionForeignKeyBuilder> context)
    {
        // The base convention must still run (it also tidies up indexes when a key's properties change); the index
        // it creates for an unwanted key is removed straight afterwards.
        base.ProcessForeignKeyAdded(foreignKeyBuilder, context);
        RemoveIfUnwanted(foreignKeyBuilder.Metadata);
    }

    public override void ProcessForeignKeyPropertiesChanged(
        IConventionForeignKeyBuilder relationshipBuilder,
        IReadOnlyList<IConventionProperty> oldDependentProperties,
        IConventionKey oldPrincipalKey,
        IConventionContext<IReadOnlyList<IConventionProperty>> context)
    {
        base.ProcessForeignKeyPropertiesChanged(relationshipBuilder, oldDependentProperties, oldPrincipalKey, context);
        RemoveIfUnwanted(relationshipBuilder.Metadata);
    }

    /// <summary>
    /// The built-in convention re-creates an index whenever one that covered a foreign key is removed. Removing the
    /// index of an unwanted key must not trigger that.
    /// </summary>
    public override void ProcessIndexRemoved(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionIndex index,
        IConventionContext<IConventionIndex> context)
    {
        bool unwanted = index.Properties.Count == 1
            && Unwanted.Any(u => u.Entity == entityTypeBuilder.Metadata.ClrType && u.Property == index.Properties[0].Name);

        if (!unwanted)
        {
            base.ProcessIndexRemoved(entityTypeBuilder, index, context);
        }
    }

    private static void RemoveIfUnwanted(IConventionForeignKey foreignKey)
    {
        if (!IsUnwanted(foreignKey))
        {
            return;
        }

        IConventionEntityType entityType = foreignKey.DeclaringEntityType;
        IConventionIndex? index = entityType.FindIndex(foreignKey.Properties);

        if (index is not null && !index.IsUnique && index.GetConfigurationSource() == ConfigurationSource.Convention)
        {
            entityType.RemoveIndex(index);
        }
    }

    private static bool IsUnwanted(IConventionForeignKey foreignKey) =>
        foreignKey.Properties.Count == 1
        && Unwanted.Any(u => u.Entity == foreignKey.DeclaringEntityType.ClrType && u.Property == foreignKey.Properties[0].Name);
}
