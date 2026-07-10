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

    /// <summary>
    /// Whether the existence of files referenced by <see cref="ResourceType.TextFile"/> / <see cref="ResourceType.Binary"/>
    /// resources is verified at compile time. Can be overridden per resource via <see cref="Resource.ValidateFilePath"/>.
    /// </summary>
    public bool ValidateFilePaths { get; set; } = true;

    // Matches {placeholder} where the name is a valid C# identifier.
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    // Sentinel tick values understood by the generated file cache.
    private const long NoCacheTicks = 0;
    private const long InfiniteCacheTicks = -1;

    public string ToCode()
    {
        var sb = new StringBuilder();

        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {Namespace ?? DefaultNamespace};");
        sb.AppendLine($"{ClassAccessModifier.ToString().ToLowerInvariant()} static class {Class ?? DefaultClass}");
        sb.AppendLine("{");
        foreach (var resource in Resources)
        {
            if (resource.IsFileResource)
            {
                AppendFileResource(sb, resource);
                continue;
            }

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

        if (Resources.Any(r => r.IsFileResource))
            AppendFileCache(sb);

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

    private void AppendFileResource(StringBuilder sb, Resource resource)
    {
        var (returnType, loader, clone) = resource.Type switch
        {
            ResourceType.Binary => ("byte[]", "global::System.IO.File.ReadAllBytes", true),
            _ => ("string", "global::System.IO.File.ReadAllText", false)
        };

        TryGetTimeoutTicks(resource.CacheTimeout, out var ticks);

        AppendSummary(sb, resource);

        var load = $"__Files.Load({BuildPathSwitch(resource)}, {ticks}L, {loader})";
        if (clone)
            load = $"({returnType}){load}.Clone()";

        sb.AppendLine($"    public static {returnType} {resource.Name} => {load};");
        sb.AppendLine();
    }

    // A culture switch that yields the file path (verbatim string literal) for each translation, defaulting to Default.
    private string BuildPathSwitch(Resource resource)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{CultureDefinition} switch");
        sb.AppendLine("    {");
        foreach (var translation in resource.Translations)
        {
            var path = (translation.Value ?? string.Empty).Replace("\"", "\"\"");
            foreach (var languageKey in translation.Key.Split(','))
                sb.AppendLine($"        \"{languageKey}\" => @\"{path}\",");
        }
        sb.AppendLine($"        _ => @\"{(resource.Default ?? string.Empty).Replace("\"", "\"\"")}\"");
        sb.Append("    }");
        return sb.ToString();
    }

    // Emits a nested, private, thread-safe file cache. Nested + private avoids name collisions when a
    // project has multiple *.res.json files (each generates its own class with its own cache).
    private static void AppendFileCache(StringBuilder sb)
    {
        sb.AppendLine("    private static class __Files");
        sb.AppendLine("    {");
        sb.AppendLine("        private sealed class Entry { public object? Value; public global::System.Threading.Timer? Timer; }");
        sb.AppendLine("        private static readonly global::System.Collections.Generic.Dictionary<string, Entry> _cache = new();");
        sb.AppendLine("        private static readonly object _sync = new();");
        sb.AppendLine();
        // timeoutTicks: 0 = no caching, negative = cache for the application lifetime, positive = cache
        // duration in ticks. A finite entry is removed by a timer when it expires, so its memory is freed
        // even if it is never accessed again - which is why a cached entry is always still valid on lookup.
        sb.AppendLine("        public static T Load<T>(string path, long timeoutTicks, global::System.Func<string, T> loader)");
        sb.AppendLine("        {");
        // Relative paths resolve against the app base directory (next to the assembly), not the
        // working directory, so referenced files should be copied to the output directory.
        sb.AppendLine("            if (!global::System.IO.Path.IsPathRooted(path))");
        sb.AppendLine("                path = global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, path);");
        sb.AppendLine();
        sb.AppendLine("            if (timeoutTicks == 0)");
        sb.AppendLine("                return loader(path);");
        sb.AppendLine();
        sb.AppendLine("            lock (_sync)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (_cache.TryGetValue(path, out var entry))");
        sb.AppendLine("                    return (T)entry.Value!;");
        sb.AppendLine();
        sb.AppendLine("                var value = loader(path);");
        sb.AppendLine("                entry = new Entry { Value = value };");
        sb.AppendLine("                if (timeoutTicks > 0)");
        sb.AppendLine("                    entry.Timer = new global::System.Threading.Timer(Evict, path, new global::System.TimeSpan(timeoutTicks), global::System.Threading.Timeout.InfiniteTimeSpan);");
        sb.AppendLine("                _cache[path] = entry;");
        sb.AppendLine("                return (T)value;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void Evict(object? key)");
        sb.AppendLine("        {");
        sb.AppendLine("            lock (_sync)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (_cache.TryGetValue((string)key!, out var entry))");
        sb.AppendLine("                {");
        sb.AppendLine("                    entry.Timer?.Dispose();");
        sb.AppendLine("                    _cache.Remove((string)key!);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    // Parses a CacheTimeout value into tick sentinels. Returns false only when a non-empty value is malformed.
    internal static bool TryGetTimeoutTicks(string? value, out long ticks)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ticks = NoCacheTicks;
            return true;
        }

        if (string.Equals(value!.Trim(), "infinite", StringComparison.OrdinalIgnoreCase))
        {
            ticks = InfiniteCacheTicks;
            return true;
        }

        if (TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var timeout) && timeout > TimeSpan.Zero)
        {
            ticks = timeout.Ticks;
            return true;
        }

        ticks = NoCacheTicks;
        return false;
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

    public ResourceType Type { get; set; } = ResourceType.Text;

    /// <summary>
    /// For file-backed resources: how long a loaded file is cached. A TimeSpan string (e.g. "00:05:00"),
    /// "infinite" to cache for the application lifetime, or null/absent to read from disk on every access.
    /// </summary>
    public string? CacheTimeout { get; set; }

    /// <summary>Per-resource override of <see cref="ReGenSourceConfig.ValidateFilePaths"/>. Null inherits the config value.</summary>
    public bool? ValidateFilePath { get; set; }

    public bool IsFileResource => Type is ResourceType.TextFile or ResourceType.Binary;

    /// <summary>Every path this resource points at (the default and each translation), skipping nulls.</summary>
    public IEnumerable<string> FilePaths()
    {
        if (Default is not null)
            yield return Default;

        foreach (var translation in Translations.Values)
            if (translation is not null)
                yield return translation;
    }
}
