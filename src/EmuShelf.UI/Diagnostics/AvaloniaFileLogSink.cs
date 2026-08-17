using System.Text;
using Avalonia.Logging;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Diagnostics;

/// <summary>
/// Routes Avalonia's own framework log — GL backend negotiation, context creation, platform errors —
/// into EmuShelf's portable <c>Logs/</c> file through <see cref="IAppLogger"/>.
/// </summary>
/// <remarks>
/// The default <c>.LogToTrace()</c> sink writes to <see cref="System.Diagnostics.Trace"/>, which has
/// no listener in an AppImage launched from Steam Game Mode. So on the Steam Deck every reason a GL
/// context failed to come up — including Avalonia's own "Unable to initialize OpenGL: … does not
/// support multithreaded context sharing" — was discarded, leaving only EmuShelf's watchdog timeout
/// with no stated cause. Bridging the framework log into the same file we already ask users for makes
/// the next launch self-diagnosing. See DECISIONS 2026-08-16.
///
/// Filtered to keep the file readable: any <see cref="LogEventLevel.Error"/> anywhere is kept, plus
/// <see cref="LogEventLevel.Information"/>+ for rendering/platform areas (the GL diagnosis). Ordinary
/// binding/layout chatter at Warning and below is dropped.
/// </remarks>
internal sealed class AvaloniaFileLogSink : ILogSink
{
    private readonly IAppLogger _logger;

    public AvaloniaFileLogSink(IAppLogger logger) => _logger = logger;

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Error
        || (level >= LogEventLevel.Information && IsDiagnosticArea(area));

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Emit(level, area, messageTemplate, null);

    public void Log(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate,
        params object?[] propertyValues) =>
        Emit(level, area, messageTemplate, propertyValues);

    private void Emit(LogEventLevel level, string area, string messageTemplate, object?[]? values)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        try
        {
            var message = $"[Avalonia/{area}] {Format(messageTemplate, values)}";
            switch (level)
            {
                case >= LogEventLevel.Error:
                    _logger.Error(message);
                    break;
                case LogEventLevel.Warning:
                    _logger.Warning(message);
                    break;
                default:
                    _logger.Information(message);
                    break;
            }
        }
        catch
        {
            // Diagnostics must never become a new failure mode: this sink is called from inside the
            // shelf's OnOpenGlInit try-block, where a throw would turn a successful GL init into the
            // flat-cover fallback. Match FileAppLogger and swallow.
        }
    }

    // EmuShelf's own scene diagnostics (see MediaShelf3DControl) plus the framework's rendering and
    // windowing-platform areas — the ones that carry a GL-init failure's cause.
    private static bool IsDiagnosticArea(string area) =>
        area.StartsWith("EmuShelf", StringComparison.OrdinalIgnoreCase)
        || area.Contains("OpenGL", StringComparison.OrdinalIgnoreCase)
        || area.Contains("Egl", StringComparison.OrdinalIgnoreCase)
        || area.Contains("Glx", StringComparison.OrdinalIgnoreCase)
        || area.Contains("Vulkan", StringComparison.OrdinalIgnoreCase)
        || area.Contains("X11", StringComparison.OrdinalIgnoreCase)
        || area.Contains("Platform", StringComparison.OrdinalIgnoreCase);

    // Avalonia message templates use {Name} placeholders filled positionally, and Avalonia ships no
    // public formatter, so substitute each successive {…} token with the next property value. Doubled
    // braces ({{, }}) are literal escapes and are left untouched.
    private static string Format(string template, object?[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return template;
        }

        var builder = new StringBuilder(template.Length + 16);
        var next = 0;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] != '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close > 0)
                {
                    builder.Append(next < values.Length ? values[next]?.ToString() ?? "null" : string.Empty);
                    next++;
                    i = close;
                    continue;
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
