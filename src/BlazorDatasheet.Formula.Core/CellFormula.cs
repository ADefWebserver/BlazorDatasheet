﻿using BlazorDatasheet.Formula.Core.Interpreter.References;
using BlazorDatasheet.Formula.Core.Interpreter.Syntax;

namespace BlazorDatasheet.Formula.Core;

public class CellFormula
{
    internal readonly SyntaxTree ExpressionTree;
    public IEnumerable<Reference>? References => ExpressionTree?.References;

    internal CellFormula(SyntaxTree expressionTree)
    {
        ExpressionTree = expressionTree;
    }

    public bool IsValid()
    {
        return !ExpressionTree.Diagnostics.Any();
    }

    public string ToFormulaString() => "=" + ExpressionTree.Root.ToExpressionText();
}