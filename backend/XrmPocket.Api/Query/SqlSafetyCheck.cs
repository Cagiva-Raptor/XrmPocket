using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace XrmPocket.Api.Query;

/// <summary>
/// Controle vóór uitvoering. Toegestaan: SELECT, DML (INSERT/UPDATE/DELETE) en de
/// procedurele T-SQL-statements die in scripts horen (DECLARE/SET, IF, WHILE, BEGIN/END,
/// TRY/CATCH, cursors, PRINT, EXECUTE AS, BREAK/CONTINUE). DDL, EXEC en alles
/// onbekends wordt geweigerd. De controle loopt door de volledige boom, dus ook
/// statements binnen IF- en WHILE-blokken.
/// </summary>
public static class SqlSafetyCheck
{
    private static readonly HashSet<Type> AllowedStatements = new()
    {
        typeof(SelectStatement),
        typeof(InsertStatement),
        typeof(UpdateStatement),
        typeof(DeleteStatement),
        typeof(SetVariableStatement),
        typeof(DeclareVariableStatement),
        typeof(DeclareCursorStatement),
        typeof(UseStatement),
        typeof(IfStatement),
        typeof(WhileStatement),
        typeof(BeginEndBlockStatement),
        typeof(BeginEndAtomicBlockStatement),
        typeof(TryCatchStatement),
        typeof(PrintStatement),
        typeof(ExecuteAsStatement),
        typeof(FetchCursorStatement),
        typeof(OpenCursorStatement),
        typeof(CloseCursorStatement),
        typeof(DeallocateCursorStatement),
        typeof(BreakStatement),
        typeof(ContinueStatement),
    };

    public static bool Check(string sql, out bool isWrite, out string? reason)
    {
        reason = null;
        isWrite = false;

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);

        if (errors.Count > 0)
        {
            // Syntaxisfouten laat de engine zelf melden; niet hier afkappen.
            return true;
        }

        var visitor = new StatementVisitor();
        fragment.Accept(visitor);

        if (visitor.Rejected != null)
        {
            reason = $"Statement '{visitor.Rejected}' wordt niet ondersteund in deze versie.";
            return false;
        }

        isWrite = visitor.HasWrite;
        return true;
    }

    private sealed class StatementVisitor : TSqlFragmentVisitor
    {
        public string? Rejected { get; private set; }
        public bool HasWrite { get; private set; }

        public override void Visit(TSqlStatement node)
        {
            if (Rejected != null)
                return;

            var type = node.GetType();

            if (type == typeof(InsertStatement) || type == typeof(UpdateStatement) || type == typeof(DeleteStatement))
                HasWrite = true;

            if (!AllowedStatements.Contains(type))
                Rejected = type.Name;

            base.Visit(node);
        }
    }
}
