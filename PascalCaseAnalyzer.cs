using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Gufel.LogTemplateAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PascalCaseAnalyzer : DiagnosticAnalyzer
    {
        private const string DiagnosticId = "GLT001";
        private const string Title = "Log template property must use PascalCase";
        private const string MessageFormat = "Log template property '{0}' should use PascalCase (suggested: '{1}')";
        private const string Description = "All properties in log message templates must follow PascalCase naming convention.";
        private const string Category = "Naming";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        // Common logging method names to check
        private static readonly string[] LoggingMethods = {
        "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical",
        "Trace", "Debug", "Information", "Warning", "Error", "Fatal", "Critical",
        "Info", "Warn", "Log"
    };

        // Regex to find template properties in curly braces
        // Updated to handle destructuring (@), alignment (:), and format specifiers
        private static readonly Regex TemplatePropertyRegex =
            new Regex(@"\{(@?)([^}:,]+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            if (!IsLoggingMethodCall(invocation))
                return;

            var arguments = invocation.ArgumentList?.Arguments;
            if (arguments == null || !arguments.Value.Any())
                return;

            // Find the message template argument (usually the first string argument)
            var templateArgument = FindMessageTemplateArgument(arguments.Value);
            if (templateArgument == null)
                return;

            AnalyzeLogTemplate(context, templateArgument);
        }

        private static bool IsLoggingMethodCall(InvocationExpressionSyntax invocation)
        {
            string methodName = GetMethodName(invocation);
            return !string.IsNullOrEmpty(methodName) &&
                   LoggingMethods.Any(m => methodName.EndsWith(m, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetMethodName(InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name.Identifier.ValueText;
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;
                default:
                    return null;
            }
        }

        private static ArgumentSyntax FindMessageTemplateArgument(SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            // Skip Exception parameter if it's first (common pattern: LogError(ex, "message", ...))
            var startIndex = 0;
            if (arguments.Count > 0)
            {
                var firstArg = arguments[0];
                var semanticModel = firstArg.SyntaxTree.GetRoot().SyntaxTree.GetCompilationUnitRoot();
                // Simple heuristic: if first arg is identifier (not string literal), likely an exception
                if (firstArg.Expression is IdentifierNameSyntax ||
                    firstArg.Expression is MemberAccessExpressionSyntax)
                {
                    startIndex = 1;
                }
            }

            // Look for the message template argument (string literal or interpolated string)
            for (int i = startIndex; i < arguments.Count; i++)
            {
                var arg = arguments[i];

                if (arg.Expression is LiteralExpressionSyntax literal &&
                    literal.Token.IsKind(SyntaxKind.StringLiteralToken))
                {
                    return arg;
                }

                // Check for interpolated strings (but skip them - they don't use template syntax)
                if (arg.Expression is InterpolatedStringExpressionSyntax)
                {
                    // Interpolated strings use different syntax, don't analyze them
                    return null;
                }
            }
            return null;
        }

        private static void AnalyzeLogTemplate(SyntaxNodeAnalysisContext context, ArgumentSyntax templateArgument)
        {
            var templateText = ExtractTemplateText(templateArgument);
            if (string.IsNullOrEmpty(templateText))
                return;

            // Skip analysis if template contains nameof() expressions
            if (ContainsNameofExpression(templateArgument, templateText))
                return;

            var matches = TemplatePropertyRegex.Matches(templateText);

            foreach (Match match in matches)
            {
                var destructuringPrefix = match.Groups[1].Value; // @ symbol
                var propertyName = match.Groups[2].Value.Trim();

                if (string.IsNullOrEmpty(propertyName))
                    continue;

                // Skip numeric positional parameters like {0}, {1}, etc.
                if (int.TryParse(propertyName, out _))
                    continue;

                // Skip properties with destructuring (@) - they're valid Serilog syntax
                if (!string.IsNullOrEmpty(destructuringPrefix))
                    continue;

                if (!IsPascalCase(propertyName))
                {
                    var suggestedName = ToPascalCase(propertyName);
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        templateArgument.GetLocation(),
                        propertyName,
                        suggestedName);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static bool ContainsNameofExpression(ArgumentSyntax templateArgument, string templateText)
        {
            // Check if the argument contains nameof expressions by analyzing the syntax tree
            // This handles cases like: $"Roles synced to Pam, {nameof(syncResult.SyncedCount)}: {syncResult.SyncedCount}"

            var descendants = templateArgument.DescendantNodes();

            // Look for nameof invocations in interpolated strings or concatenated expressions
            foreach (var node in descendants)
            {
                if (node is InvocationExpressionSyntax invocation &&
                    invocation.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "nameof")
                {
                    return true;
                }
            }

            // Also check for patterns where nameof might be in the template text
            // This is a simple heuristic for string literals that might have been built with nameof
            if (templateText.Contains("nameof"))
            {
                return true;
            }

            return false;
        }

        private static string ExtractTemplateText(ArgumentSyntax templateArgument)
        {
            if (templateArgument.Expression is LiteralExpressionSyntax syntax)
            {
                return syntax.Token.ValueText;
            }

            return null;
        }

        private static bool IsPascalCase(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            // Must start with uppercase letter
            if (!char.IsUpper(identifier[0]))
                return false;

            // Check for valid PascalCase pattern
            // Allow letters, digits, and underscores, but no consecutive underscores or leading/trailing underscores
            for (int i = 1; i < identifier.Length; i++)
            {
                char c = identifier[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }

            // No leading or trailing underscores
            if (identifier.StartsWith("_") || identifier.EndsWith("_"))
                return false;

            return true;
        }

        private static string ToPascalCase(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return identifier;

            // Handle common cases
            var result = identifier;

            // Convert first character to uppercase
            if (char.IsLower(result[0]))
            {
                result = char.ToUpper(result[0]) + result.Substring(1);
            }

            // Handle camelCase to PascalCase conversion
            if (identifier.Length > 1 && char.IsLower(identifier[0]) && char.IsUpper(identifier[1]))
            {
                result = char.ToUpper(identifier[0]) + identifier.Substring(1);
            }

            // Handle snake_case to PascalCase
            if (result.Contains('_'))
            {
                var parts = result.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
                result = string.Join("", parts.Select(part =>
                    char.ToUpper(part[0]) + (part.Length > 1 ? part.Substring(1).ToLower() : "")));
            }

            // Handle kebab-case to PascalCase
            if (result.Contains('-'))
            {
                var parts = result.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                result = string.Join("", parts.Select(part =>
                    char.ToUpper(part[0]) + (part.Length > 1 ? part.Substring(1).ToLower() : "")));
            }

            return result;
        }
    }
}
