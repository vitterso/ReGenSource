using System.Text;

namespace ReGenSource;

internal sealed class ReGenSourceConfig
{
    public const string DefaultNamespace = "ReGenSource";
    public const string DefaultClass = "Resources";

    public ClassAccessModifier ClassAccessModifier { get; set; } = ClassAccessModifier.Public;

    public string? Namespace { get; set; }

    public string? Class { get; set; }

    public List<Resource> Resources { get; set; } = [];

    public string CultureDefinition { get; set; } = "global::System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName";

    public string ToCode()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"namespace {Namespace ?? DefaultNamespace};");
        sb.AppendLine($"{ClassAccessModifier.ToString().ToLowerInvariant()} static class {Class ?? DefaultClass}");
        sb.AppendLine("{");
        foreach (var resource in Resources)
        {
            foreach (var translation in resource.Translations)
            {
                sb.AppendLine($"    private const string _{resource.Name}_{translation.Key} = @\"{translation.Value.Replace("\"", "\"\"")}\";");
            }
            sb.AppendLine($"    private const string _{resource.Name}_default = @\"{resource.Default?.Replace("\"", "\"\"")}\";");

            sb.AppendLine();
            sb.AppendLine("    /// <summary>");

            var hint = resource.Default?
                .Substring(0, Math.Min(20, resource.Default.Length))
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);

            sb.AppendLine($"    /// Localized string for the current culture, e.g. \"{hint}...\"");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static string {resource.Name} => {CultureDefinition} switch");
            sb.AppendLine("    {");
            foreach (var translation in resource.Translations)
            {
                sb.AppendLine($"        \"{translation.Key}\" => _{resource.Name}_{translation.Key},");
            }
            sb.AppendLine($"        _ => _{resource.Name}_default");
            sb.AppendLine("    };");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }
}

internal sealed class Resource
{
    public string? Name { get; set; }

    public string? Default { get; set; }

    public Dictionary<string, string> Translations { get; set; } = [];
}
