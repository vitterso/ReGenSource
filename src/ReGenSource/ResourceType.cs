namespace ReGenSource;

internal enum ResourceType
{
    /// <summary>An inline localized string (the default).</summary>
    Text,

    /// <summary>A file whose text contents are read at runtime and exposed as a <see cref="string"/>.</summary>
    TextFile,

    /// <summary>A file whose raw bytes are read at runtime and exposed as a <c>byte[]</c>.</summary>
    Binary
}
