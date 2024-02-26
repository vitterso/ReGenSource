using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ReGenSource;

[Generator]
internal class ResourceGenerator : ISourceGenerator
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var configFiles = context.AdditionalFiles.Where(f => f.Path.EndsWith(".json"));
        foreach (var configFile in configFiles)
        {
            var text = configFile.GetText()?.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                context.ReportDiagnostic(Diagnostics.EmptyJsonFile(configFile.Path));
                continue;
            }

            var (config, exception) = TryDeserializeConfig(text);
            if (config is null || exception is not null)
            {
                context.ReportDiagnostic(Diagnostics.InvalidJsonFile(configFile.Path, exception));
                continue;
            }

            var sourceFileName = $"{config.Namespace ?? ReGenSourceConfig.DefaultNamespace}.{config.Class ?? ReGenSourceConfig.DefaultClass}.g.cs";
            context.AddSource(sourceFileName, SourceText.From(config.ToCode(), Encoding.UTF8));
        }
    }

    private (ReGenSourceConfig? Config, Exception? Exception) TryDeserializeConfig(string text)
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
