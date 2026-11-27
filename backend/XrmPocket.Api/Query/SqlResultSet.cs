using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace XrmPocket.Api.Query;

internal static class SqlResultSet
{
    public static bool ContainsSelect(string sql)
    {
        var parser = new TSql170Parser(true, SqlEngineType.All);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
            return true;

        var visitor = new SelectVisitor();
        script.Accept(visitor);
        return visitor.Found;
    }

    private sealed class SelectVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void ExplicitVisit(SelectStatement node)
        {
            Found = true;
            base.ExplicitVisit(node);
        }
    }
}
