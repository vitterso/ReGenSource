using Microsoft.CodeAnalysis;

namespace ReGenSource;

internal static class Diagnostics
{
    public static Diagnostic EmptyJsonFile(string filePath) => Diagnostic.Create(
        new DiagnosticDescriptor(
            "RESGEN001",
            "Empty JSON file",
            $"The JSON file '{filePath}' is empty",
            "JSON",
            DiagnosticSeverity.Warning,
            true),
        null);

    public static Diagnostic InvalidJsonFile(string filePath, Exception? exception = null)
    {
        var message = $"The JSON file '{filePath}' cannot be deserialized to a valid configuration object";
        if (exception is not null)
            message += $": {exception}";

        return Diagnostic.Create(
            new DiagnosticDescriptor(
                "RESGEN002",
                "Invalid JSON file",
                message,
                "JSON",
                DiagnosticSeverity.Warning,
                true),
            null);
    }
}
