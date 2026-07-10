using System.Text;
using System.Text.RegularExpressions;

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

    // Matches {placeholder} where the name is a valid C# identifier.
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public string ToCode()
    {
        var sb = new StringBuilder();

        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {Namespace ?? DefaultNamespace};");
        sb.AppendLine($"{ClassAccessModifier.ToString().ToLowerInvariant()} static class {Class ?? DefaultClass}");
        sb.AppendLine("{");
        foreach (var resource in Resources)
        {
            var parameters = GetParameters(resource);
            if (parameters.Count == 0)
            {
                AppendConstantResource(sb, resource);
            }
            else
            {
                AppendFormattedResource(sb, resource, parameters);
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private void AppendConstantResource(StringBuilder sb, Resource resource)
    {
        foreach (var translation in resource.Translations)
        {
            var concatenatedLanguageKeys = string.Join("_", translation.Key.Split(','));
            sb.AppendLine($"    private const string _{resource.Name}_{concatenatedLanguageKeys} = @\"{translation.Value.Replace("\"", "\"\"")}\";");
        }
        sb.AppendLine($"    private const string _{resource.Name}_default = @\"{resource.Default?.Replace("\"", "\"\"")}\";");

        AppendSummary(sb, resource);
        sb.AppendLine($"    public static string {resource.Name} => {BuildCultureSwitch(resource)};");
        sb.AppendLine();
    }

    private void AppendFormattedResource(StringBuilder sb, Resource resource, List<string> parameters)
    {
        foreach (var translation in resource.Translations)
        {
            var concatenatedLanguageKeys = string.Join("_", translation.Key.Split(','));
            var value = ToFormatString(translation.Value, parameters).Replace("\"", "\"\"");
            sb.AppendLine($"    private const string _{resource.Name}_{concatenatedLanguageKeys} = @\"{value}\";");
        }
        var defaultValue = ToFormatString(resource.Default, parameters).Replace("\"", "\"\"");
        sb.AppendLine($"    private const string _{resource.Name}_default = @\"{defaultValue}\";");

        AppendSummary(sb, resource);

        var parameterList = string.Join(", ", parameters.Select(p => $"object? @{p}"));
        var argumentList = string.Join(", ", parameters.Select(p => $"@{p}"));
        sb.AppendLine($"    public static string {resource.Name}({parameterList}) => string.Format({BuildCultureSwitch(resource)}, {argumentList});");
        sb.AppendLine();
    }

    private static void AppendSummary(StringBuilder sb, Resource resource)
    {
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");

        var hint = resource.Default?
            .Substring(0, Math.Min(20, resource.Default.Length))
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);

        sb.AppendLine($"    /// Localized string for the current culture, e.g. \"{hint}...\"");
        sb.AppendLine("    /// </summary>");
    }

    private string BuildCultureSwitch(Resource resource)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{CultureDefinition} switch");
        sb.AppendLine("    {");
        foreach (var translation in resource.Translations)
        {
            var languageKeys = translation.Key.Split(',');
            var concatenatedLanguageKeys = string.Join("_", languageKeys);
            foreach (var languageKey in languageKeys)
            {
                sb.AppendLine($"        \"{languageKey}\" => _{resource.Name}_{concatenatedLanguageKeys},");
            }
        }
        sb.AppendLine($"        _ => _{resource.Name}_default");
        sb.Append("    }");
        return sb.ToString();
    }

    // Distinct placeholder names across the default and all translations, in order of first appearance.
    private static List<string> GetParameters(Resource resource)
    {
        var parameters = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Scan(resource.Default);
        foreach (var translation in resource.Translations)
            Scan(translation.Value);

        return parameters;

        void Scan(string? text)
        {
            if (text is null)
                return;

            foreach (Match match in PlaceholderRegex.Matches(text))
            {
                var name = match.Groups[1].Value;
                if (seen.Add(name))
                    parameters.Add(name);
            }
        }
    }

    // Converts a raw string into a string.Format template: named placeholders become positional
    // indices, and any other literal braces are escaped ({{ / }}).
    private static string ToFormatString(string? raw, List<string> parameters)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var sb = new StringBuilder();
        var index = 0;
        while (index < raw!.Length)
        {
            var current = raw[index];
            switch (current)
            {
                case '{':
                    var match = PlaceholderRegex.Match(raw, index);
                    if (match.Success && match.Index == index)
                    {
                        sb.Append('{').Append(parameters.IndexOf(match.Groups[1].Value)).Append('}');
                        index += match.Length;
                        continue;
                    }

                    sb.Append("{{");
                    index++;
                    continue;
                case '}':
                    sb.Append("}}");
                    index++;
                    continue;
                default:
                    sb.Append(current);
                    index++;
                    break;
            }
        }

        return sb.ToString();
    }
}

internal sealed class Resource
{
    public string? Name { get; set; }

    public string? Default { get; set; }

    public Dictionary<string, string> Translations { get; set; } = [];
}
