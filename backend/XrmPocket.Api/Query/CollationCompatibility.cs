using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace XrmPocket.Api.Query;

/// <summary>
/// SQL 4 CDS assigns different SqlString collations to declared string variables.
/// Normalize text equality to numeric operations; this avoids explicit COLLATE,
/// which is not supported by the engine's runtime.
/// </summary>
internal static class CollationCompatibility
{
    public static string Normalize(string sql)
    {
        var parser = new TSql170Parser(true, SqlEngineType.All);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
            return sql;

        var collector = new TextComparisonCollector(sql);
        script.Accept(collector);

        var result = sql;
        var edits = collector.Edits
            .GroupBy(x => (x.Offset, x.Length))
            .Select(x => x.First())
            .ToArray();
        foreach (var edit in edits
                     .Where(edit => !edits.Any(other =>
                         other.Offset <= edit.Offset &&
                         other.Offset + other.Length >= edit.Offset + edit.Length &&
                         other.Length > edit.Length))
                     .OrderByDescending(x => x.Offset))
            result = result.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
        return result;
    }

    private sealed class TextComparisonCollector(string sql) : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _stringVariables = new(StringComparer.OrdinalIgnoreCase);
        public List<(int Offset, int Length, string Replacement)> Edits { get; } = [];

        public override void ExplicitVisit(DeclareVariableElement node)
        {
            if (node.DataType is SqlDataTypeReference dataType && IsStringType(dataType.SqlDataTypeOption))
                _stringVariables.Add(node.VariableName.Value);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType is BooleanComparisonType.Equals or
                BooleanComparisonType.NotEqualToBrackets or
                BooleanComparisonType.NotEqualToExclamation)
            {
                var leftVariable = IsStringVariable(node.FirstExpression);
                var rightVariable = IsStringVariable(node.SecondExpression);
                var variable = leftVariable ? Text(node.FirstExpression) : rightVariable ? Text(node.SecondExpression) : null;
                var value = leftVariable ? Text(node.SecondExpression) : rightVariable ? Text(node.FirstExpression) : null;

                if (variable is not null && value is not null)
                {
                    var comparesWithEmpty = (leftVariable && node.SecondExpression is StringLiteral { Value: "" }) ||
                                            (rightVariable && node.FirstExpression is StringLiteral { Value: "" });
                    var equal = comparesWithEmpty
                        ? $"(LEN({variable}) = 0)"
                        : $"(LEN({variable}) = LEN({value}) AND CHARINDEX({value}, {variable}) = 1)";
                    var replacement = node.ComparisonType == BooleanComparisonType.Equals ? equal : $"NOT {equal}";
                    Edits.Add((node.StartOffset, node.FragmentLength, replacement));
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(BinaryExpression node)
        {
            if (node.BinaryExpressionType == BinaryExpressionType.Add && IsTextConcatenation(node))
                Edits.Add((node.StartOffset, node.FragmentLength, FormatMessage(node)));
            base.ExplicitVisit(node);
        }

        private bool IsTextConcatenation(BinaryExpression node)
        {
            var fragments = Flatten(node).ToArray();
            return fragments.Any(x => x is StringLiteral) && fragments.Any(x => x is not StringLiteral);
        }

        private string FormatMessage(BinaryExpression node)
        {
            var format = new List<string>();
            var arguments = new List<string>();
            foreach (var part in Flatten(node))
            {
                if (part is StringLiteral literal)
                    format.Add(literal.Value.Replace("%", "%%").Replace("'", "''"));
                else
                {
                    format.Add("%s");
                    arguments.Add(Text(part));
                }
            }
            var args = arguments.Count == 0 ? "" : ", " + string.Join(", ", arguments);
            return $"FORMATMESSAGE('{string.Concat(format)}'{args})";
        }

        private static IEnumerable<ScalarExpression> Flatten(ScalarExpression expression)
        {
            if (expression is BinaryExpression binary && binary.BinaryExpressionType == BinaryExpressionType.Add)
            {
                foreach (var item in Flatten(binary.FirstExpression)) yield return item;
                foreach (var item in Flatten(binary.SecondExpression)) yield return item;
            }
            else yield return expression;
        }

        private bool IsStringVariable(ScalarExpression expression)
        {
            var visitor = new StringVariableVisitor(_stringVariables);
            expression.Accept(visitor);
            return visitor.Found;
        }

        private sealed class StringVariableVisitor(HashSet<string> variables) : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(VariableReference node)
            {
                if (variables.Contains(node.Name)) Found = true;
                base.ExplicitVisit(node);
            }
        }

        private string Text(TSqlFragment fragment) =>
            sql.Substring(fragment.StartOffset, fragment.FragmentLength);

        private static bool IsStringType(SqlDataTypeOption option) => option is
            SqlDataTypeOption.Char or SqlDataTypeOption.VarChar or
            SqlDataTypeOption.NChar or SqlDataTypeOption.NVarChar or
            SqlDataTypeOption.Text or SqlDataTypeOption.NText;
    }
}
