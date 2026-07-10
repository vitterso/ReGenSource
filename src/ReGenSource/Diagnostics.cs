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

    public static Diagnostic ReferencedFileNotFound(string fileName, string? resourceName, string resourcePath, string resolvedPath) => Diagnostic.Create(
        new DiagnosticDescriptor(
            "RESGEN003",
            "Referenced file not found",
            $"Resource '{resourceName}' in '{fileName}' references the file '{resourcePath}' (resolved to '{resolvedPath}'), which is not among the project's AdditionalFiles. Add it as an <AdditionalFiles> item, or disable the check with \"validateFilePaths\": false.",
            "JSON",
            DiagnosticSeverity.Warning,
            true),
        null);

    public static Diagnostic InvalidCacheTimeout(string fileName, string? resourceName, string value) => Diagnostic.Create(
        new DiagnosticDescriptor(
            "RESGEN004",
            "Invalid cache timeout",
            $"Resource '{resourceName}' in '{fileName}' has an invalid cacheTimeout '{value}'; expected a TimeSpan string (e.g. \"00:05:00\") or \"infinite\". Caching is disabled for this resource.",
            "JSON",
            DiagnosticSeverity.Warning,
            true),
        null);
}
