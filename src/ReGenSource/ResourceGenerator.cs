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
        var configTexts = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".res.json"))
            .Select((text, cancellationToken) => (Name: Path.GetFileNameWithoutExtension(text.Path), Content: text.GetText(cancellationToken)?.ToString()));

        context.RegisterSourceOutput(configTexts, (ctx, nameAndContent) =>
        {
            if (string.IsNullOrWhiteSpace(nameAndContent.Content))
            {
                ctx.ReportDiagnostic(Diagnostics.EmptyJsonFile(nameAndContent.Name));
                return;
            }

            var (config, exception) = TryDeserializeConfig(nameAndContent.Content!);
            if (config is null || exception is not null)
            {
                ctx.ReportDiagnostic(Diagnostics.InvalidJsonFile(nameAndContent.Name, exception));
                return;
            }

            var sourceFileName = $"{config.Namespace ?? ReGenSourceConfig.DefaultNamespace}.{config.Class ?? ReGenSourceConfig.DefaultClass}.g.cs";
            var sourceCode = config.ToCode();
            ctx.AddSource(sourceFileName, SourceText.From(sourceCode, Encoding.UTF8));
        });
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
