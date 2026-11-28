using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VehicleRental.ArchitectureTests.Support;

/// <summary>What kind of database access bypasses EF Core's query pipeline.</summary>
public enum RawAccessKind
{
    /// <summary>A connection, command or data source used directly. EF Core's command interceptors never see it.</summary>
    RawConnection,

    /// <summary>A "Raw" SQL method (<c>ExecuteSqlRaw</c>, <c>FromSqlRaw</c>, <c>SqlQueryRaw</c>): the text is sent as written, unparameterised.</summary>
    RawSql,
}

/// <summary>One place in the source that accesses the database outside LINQ.</summary>
public sealed record RawAccessSite(RawAccessKind Kind, string File, string Type, int Line, string What);

/// <summary>One place where SQL text is not provably a compile-time constant (or a safe API gets something other than an interpolated string).</summary>
public sealed record SqlTextViolation(string File, int Line, string What);

public sealed record ScanResult(IReadOnlyList<RawAccessSite> Sites, IReadOnlyList<SqlTextViolation> Violations);

/// <summary>
/// Finds raw database access in C# source by syntax (no compilation needed, so it is fast and has no false "could not
/// build" failures). It is deliberately strict and name-based: anything that looks like raw access is reported, and the
/// reviewed allowlist decides what is accepted.
/// <para>
/// Limits (by design, documented in docs/multi-tenancy/security-hardening-before-rls.md): reflection, <c>dynamic</c>, a
/// type alias that renames an Npgsql type, or a target-typed <c>new()</c> assigned to something other than a typed
/// variable are not seen. These are review-time concerns; the database (RLS from 14A-7) is the real boundary.
/// </para>
/// </summary>
public static class RawDatabaseAccessScanner
{
    private static readonly HashSet<string> RawConnectionMethods =
    [
        "GetDbConnection", "CreateCommand", "CreateBatch",
        "ExecuteReader", "ExecuteReaderAsync", "ExecuteScalar", "ExecuteScalarAsync", "ExecuteNonQuery", "ExecuteNonQueryAsync",
    ];

    private static readonly HashSet<string> RawConnectionTypes =
    [
        "NpgsqlConnection", "NpgsqlCommand", "NpgsqlBatch", "NpgsqlBatchCommand",
        "NpgsqlDataSource", "NpgsqlDataSourceBuilder", "NpgsqlSlimDataSourceBuilder", "NpgsqlMultiHostDataSource",
    ];

    // Types whose first constructor argument is SQL text.
    private static readonly HashSet<string> SqlTextConstructors = ["NpgsqlCommand", "NpgsqlBatchCommand"];

    private static readonly HashSet<string> RawSqlMethods = ["ExecuteSqlRaw", "ExecuteSqlRawAsync", "FromSqlRaw", "SqlQueryRaw"];

    // Parameterise every interpolation, but only when given an interpolated string literal. A FormattableString built
    // elsewhere (for example with FormattableStringFactory.Create and a user-supplied format) is not safe.
    private static readonly HashSet<string> InterpolatedSqlMethods =
    [
        "ExecuteSql", "ExecuteSqlAsync", "FromSql", "SqlQuery",
        "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync", "FromSqlInterpolated",
    ];

    public static ScanResult Scan(string file, string source)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(source, path: file).GetCompilationUnitRoot();
        var scan = new FileScan(file, root);

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    scan.Invocation(invocation);
                    break;
                case BaseObjectCreationExpressionSyntax creation:
                    scan.Creation(creation);
                    break;
                case AssignmentExpressionSyntax assignment when IsCommandText(assignment.Left):
                    scan.RequireConstantSql(assignment.Right, "CommandText");
                    break;
            }
        }

        return new ScanResult(scan.Sites, scan.Violations);
    }

    private static bool IsCommandText(ExpressionSyntax left) => left switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text == "CommandText",
        IdentifierNameSyntax name => name.Identifier.Text == "CommandText", // object initializer
        _ => false,
    };

    internal static string? MethodName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    private static string? TypeName(TypeSyntax? type) => type switch
    {
        QualifiedNameSyntax qualified => TypeName(qualified.Right),
        AliasQualifiedNameSyntax alias => TypeName(alias.Name),
        SimpleNameSyntax simple => simple.Identifier.Text,
        NullableTypeSyntax nullable => TypeName(nullable.ElementType),
        _ => null,
    };

    private sealed class FileScan(string file, CompilationUnitSyntax root)
    {
        private readonly HashSet<string> _constants = ConstantNames(root);
        private readonly HashSet<string> _typeNames = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text).ToHashSet();

        public List<RawAccessSite> Sites { get; } = [];

        public List<SqlTextViolation> Violations { get; } = [];

        public void Invocation(InvocationExpressionSyntax invocation)
        {
            string? name = MethodName(invocation.Expression);

            if (name is null)
            {
                return;
            }

            // NpgsqlDataSource.Create(...) and friends: a static factory on a raw type.
            if (invocation.Expression is MemberAccessExpressionSyntax { Expression: var receiver } && TypeName(receiver as TypeSyntax) is { } receiverType && RawConnectionTypes.Contains(receiverType))
            {
                Site(RawAccessKind.RawConnection, invocation, $"{receiverType}.{name}");
            }

            if (RawConnectionMethods.Contains(name))
            {
                Site(RawAccessKind.RawConnection, invocation, name);
            }

            if (RawSqlMethods.Contains(name))
            {
                Site(RawAccessKind.RawSql, invocation, name);

                if (SqlArgument(invocation.ArgumentList) is { } sql)
                {
                    RequireConstantSql(sql, name);
                }
                else
                {
                    Violation(invocation, $"{name}: no SQL argument found");
                }
            }

            if (InterpolatedSqlMethods.Contains(name))
            {
                ExpressionSyntax? sql = SqlArgument(invocation.ArgumentList);

                if (sql is not InterpolatedStringExpressionSyntax && !IsConstant(sql))
                {
                    Violation(invocation, $"{name} must be given an interpolated string literal (or constant SQL), not '{sql}'");
                }
            }
        }

        public void Creation(BaseObjectCreationExpressionSyntax creation)
        {
            string? type = creation switch
            {
                ObjectCreationExpressionSyntax explicitType => TypeName(explicitType.Type),
                ImplicitObjectCreationExpressionSyntax implicitType => DeclaredType(implicitType),
                _ => null,
            };

            if (type is null || !RawConnectionTypes.Contains(type))
            {
                return;
            }

            Site(RawAccessKind.RawConnection, creation, $"new {type}");

            if (SqlTextConstructors.Contains(type) && SqlArgument(creation.ArgumentList) is { } sql)
            {
                RequireConstantSql(sql, $"new {type}");
            }
        }

        /// <summary>
        /// SQL text must be a compile-time constant. A parameter of the enclosing method is accepted only if every call of
        /// that method in this file passes a constant for it (one level, which covers small private helpers).
        /// </summary>
        public void RequireConstantSql(ExpressionSyntax sql, string what)
        {
            if (IsConstant(sql))
            {
                return;
            }

            // Only a PRIVATE helper qualifies: anything wider could be called from another file with any text. It must also be
            // called at least once here, otherwise there is nothing to verify and the text comes from somewhere unseen.
            if (sql is IdentifierNameSyntax identifier
                && sql.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is MethodDeclarationSyntax method
                && method.Modifiers.Any(SyntaxKind.PrivateKeyword)
                && method.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList() is var parameters
                && parameters.IndexOf(identifier.Identifier.Text) is var index and >= 0
                && root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(call => MethodName(call.Expression) == method.Identifier.Text)
                    .ToList() is { Count: > 0 } calls)
            {
                foreach (InvocationExpressionSyntax call in calls)
                {
                    ExpressionSyntax? argument = ArgumentFor(call.ArgumentList, identifier.Identifier.Text, index);

                    if (!IsConstant(argument))
                    {
                        Violation(call, $"{what} via {method.Identifier.Text}({identifier.Identifier.Text}): '{argument}' is not a compile-time constant");
                    }
                }

                return;
            }

            Violation(sql, $"{what}: '{sql}' is not a compile-time constant");
        }

        private bool IsConstant(ExpressionSyntax? expression) => expression switch
        {
            LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
            ParenthesizedExpressionSyntax parenthesized => IsConstant(parenthesized.Expression),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) => IsConstant(binary.Left) && IsConstant(binary.Right),
            IdentifierNameSyntax identifier => _constants.Contains(identifier.Identifier.Text),
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } member =>
                _typeNames.Contains(owner.Identifier.Text) && _constants.Contains(member.Name.Identifier.Text),
            _ => false,
        };

        private void Site(RawAccessKind kind, SyntaxNode node, string what) =>
            Sites.Add(new RawAccessSite(kind, file, EnclosingType(node), Line(node), what));

        private void Violation(SyntaxNode node, string what) => Violations.Add(new SqlTextViolation(file, Line(node), what));

        private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        private static string EnclosingType(SyntaxNode node) =>
            node.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>()?.Identifier.Text ?? "<top-level>";

        private static string? DeclaredType(ImplicitObjectCreationExpressionSyntax creation) => creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } => TypeName(declaration.Type),
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => TypeName(property.Type),
            _ => null,
        };

        private static ExpressionSyntax? SqlArgument(BaseArgumentListSyntax? arguments) => ArgumentFor(arguments, "sql", 0);

        private static ExpressionSyntax? ArgumentFor(BaseArgumentListSyntax? arguments, string name, int position)
        {
            if (arguments is null)
            {
                return null;
            }

            ArgumentSyntax? named = arguments.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == name);

            if (named is not null)
            {
                return named.Expression;
            }

            return position < arguments.Arguments.Count && arguments.Arguments[position].NameColon is null
                ? arguments.Arguments[position].Expression
                : null;
        }

        private static HashSet<string> ConstantNames(CompilationUnitSyntax root)
        {
            IEnumerable<VariableDeclaratorSyntax> fields = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
                .Where(f => f.Modifiers.Any(SyntaxKind.ConstKeyword))
                .SelectMany(f => f.Declaration.Variables);

            IEnumerable<VariableDeclaratorSyntax> locals = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
                .Where(l => l.IsConst)
                .SelectMany(l => l.Declaration.Variables);

            return fields.Concat(locals).Select(v => v.Identifier.Text).ToHashSet();
        }
    }
}
