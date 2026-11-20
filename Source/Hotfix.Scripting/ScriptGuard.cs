using System;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hotfix.Scripting;

internal sealed class ScriptTimeoutException() : Exception("The script was stopped because it ran longer than its time limit.");

public static class ScriptGuard
{
    [ThreadStatic]
    internal static ScriptLimit? Limit;

    public static void Tick()
    {
        if (Limit is { Expired: true })
            throw new ScriptTimeoutException();
        RuntimeHelpers.EnsureSufficientExecutionStack();
    }

    internal sealed class Rewriter : CSharpSyntaxRewriter
    {
        private static readonly StatementSyntax Check = SyntaxFactory.ParseStatement("global::Hotfix.Scripting.ScriptGuard.Tick();");

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            node = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return node.WithStatement(Guarded(node.Statement));
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            node = (DoStatementSyntax)base.VisitDoStatement(node)!;
            return node.WithStatement(Guarded(node.Statement));
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            node = (ForStatementSyntax)base.VisitForStatement(node)!;
            return node.WithStatement(Guarded(node.Statement));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            node = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            return node.WithStatement(Guarded(node.Statement));
        }

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            node = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
            return node.WithStatement(Guarded(node.Statement));
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            return node.Body is { } body ? node.WithBody(Guarded(body)) : node;
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            node = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            return node.Body is { } body ? node.WithBody(Guarded(body)) : node;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            node = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
            return node.Block is { } body ? node.WithBlock(Guarded(body)) : node;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            node = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
            return node.Block is { } body ? node.WithBlock(Guarded(body)) : node;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            node = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
            return node.WithBlock(Guarded(node.Block));
        }

        private static BlockSyntax Guarded(StatementSyntax statement) => statement is BlockSyntax block
            ? block.WithStatements(block.Statements.Insert(0, Check))
            : SyntaxFactory.Block(Check, statement);
    }
}
