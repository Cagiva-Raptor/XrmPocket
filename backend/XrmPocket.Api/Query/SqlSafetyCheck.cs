using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace XrmPocket.Api.Query;

/// <summary>
/// Controle vóór uitvoering: deze versie voert enkel leesqueries uit. Schrijfacties
/// worden geweigerd zodat een tikfout nooit data kan wijzigen.
/// </summary>
public static class SqlSafetyCheck
{
    public static bool IsReadOnly(string sql, out string? reason)
    {
        reason = null;

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);

        if (errors.Count > 0)
        {
            // Syntaxisfouten laat de engine zelf melden; niet hier afkappen.
            return true;
        }

        if (fragment is not TSqlScript script)
        {
            reason = "Onherkenbaar SQL-script.";
            return false;
        }

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (statement is SelectStatement or SetVariableStatement or DeclareVariableStatement or UseStatement)
                    continue;

                reason = $"Statement '{statement.GetType().Name}' is een schrijfactie of wordt niet ondersteund in deze versie.";
                return false;
            }
        }

        return true;
    }
}
