using System.Globalization;

namespace MyApi;

internal sealed class UiCultureScope : IDisposable
{
    private readonly CultureInfo _default;

    private UiCultureScope(CultureInfo info)
    {
        _default = Thread.CurrentThread.CurrentUICulture;
        Thread.CurrentThread.CurrentUICulture = info;
    }

    internal static UiCultureScope Begin(CultureInfo info) => new(info);

    public void Dispose() => Thread.CurrentThread.CurrentUICulture = _default;
}
