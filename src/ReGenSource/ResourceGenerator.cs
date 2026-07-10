using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ReGenSource;

[Generator(LanguageNames.CSharp)]
internal sealed class ResourceGenerator : IIncrementalGenerator
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        Converters =
        {
            new JsonStringEnumConverter()
        },
        PropertyNameCaseInsensitive = true
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // All AdditionalFiles paths, used to verify referenced files exist without touching the
        // disk (RS1035 bans file IO in analyzers). Files must therefore be declared as <AdditionalFiles>.
        var additionalPaths = context.AdditionalTextsProvider
            .Select((text, _) => text.Path)
            .Collect();

        var configTexts = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".res.json"))
            .Select((text, cancellationToken) => (
                Name: Path.GetFileNameWithoutExtension(text.Path),
                Directory: Path.GetDirectoryName(text.Path),
                Content: text.GetText(cancellationToken)?.ToString()))
            .Combine(additionalPaths);

        context.RegisterSourceOutput(configTexts, (ctx, pair) =>
        {
            var (file, knownPaths) = pair;

            if (string.IsNullOrWhiteSpace(file.Content))
            {
                ctx.ReportDiagnostic(Diagnostics.EmptyJsonFile(file.Name));
                return;
            }

            var (config, exception) = TryDeserializeConfig(file.Content!);
            if (config is null || exception is not null)
            {
                ctx.ReportDiagnostic(Diagnostics.InvalidJsonFile(file.Name, exception));
                return;
            }

            ValidateResources(ctx, config, file.Name, file.Directory, knownPaths);

            var sourceFileName = $"{config.Namespace ?? ReGenSourceConfig.DefaultNamespace}.{config.Class ?? ReGenSourceConfig.DefaultClass}.g.cs";
            var sourceCode = config.ToCode();
            ctx.AddSource(sourceFileName, SourceText.From(sourceCode, Encoding.UTF8));
        });
    }

    private static void ValidateResources(SourceProductionContext ctx, ReGenSourceConfig config, string fileName, string? directory, ImmutableArray<string> knownPaths)
    {
        HashSet<string>? known = null;

        foreach (var resource in config.Resources)
        {
            if (!resource.IsFileResource)
                continue;

            if (!ReGenSourceConfig.TryGetTimeoutTicks(resource.CacheTimeout, out _))
                ctx.ReportDiagnostic(Diagnostics.InvalidCacheTimeout(fileName, resource.Name, resource.CacheTimeout!));

            if (!(resource.ValidateFilePath ?? config.ValidateFilePaths))
                continue;

            known ??= new HashSet<string>(knownPaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);

            foreach (var path in resource.FilePaths())
            {
                var resolved = directory is null ? path : Path.Combine(directory, path);
                if (!known.Contains(NormalizePath(resolved)))
                    ctx.ReportDiagnostic(Diagnostics.ReferencedFileNotFound(fileName, resource.Name, path, resolved));
            }
        }
    }

    // Resolves "." / ".." segments and unifies separators so a project-relative reference can be compared
    // to an absolute AdditionalFiles path. Pure string work only — no disk access.
    private static string NormalizePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            if (segment == ".." && segments.Count > 0 && segments[segments.Count - 1] != "..")
                segments.RemoveAt(segments.Count - 1);
            else
                segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    private static (ReGenSourceConfig? Config, Exception? Exception) TryDeserializeConfig(string text)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ReGenSourceConfig>(text, JsonSerializerOptions);
            return (config, null);
        }
        catch (Exception e)
        {
            return (null, e);
        }
    }
}
