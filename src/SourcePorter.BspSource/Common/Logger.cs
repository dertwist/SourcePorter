namespace SourcePorter.BspSource.Common;

/// <summary>Severity levels, mirroring the log4j levels BSPSource uses.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Minimal stand-in for the Apache Log4j 2 <c>Logger</c> that the Java BSPSource
/// uses pervasively (<c>private static final Logger L = LogManager.getLogger();</c>).
/// Supports the SLF4J-style <c>{}</c> placeholder substitution used at the call
/// sites. All output is funnelled through <see cref="LogManager.Sink"/> so the
/// host (SourcePorter's console) can colour-code and display it, exactly like
/// <c>ProcessRunner.OnOutput</c> does for the importer.
/// </summary>
public sealed class Logger
{
    internal Logger() { }

    public bool IsDebugEnabled => LogManager.DebugEnabled;

    public void Debug(string message, params object?[] args) => Emit(LogLevel.Debug, message, null, args);
    public void Info(string message, params object?[] args) => Emit(LogLevel.Info, message, null, args);
    public void Warn(string message, params object?[] args) => Emit(LogLevel.Warn, message, null, args);
    public void Error(string message, params object?[] args) => Emit(LogLevel.Error, message, null, args);

    public void Warn(string message, Exception ex) => Emit(LogLevel.Warn, message, ex, []);
    public void Error(string message, Exception ex) => Emit(LogLevel.Error, message, ex, []);

    private static void Emit(LogLevel level, string message, Exception? ex, object?[] args)
    {
        if (level == LogLevel.Debug && !LogManager.DebugEnabled)
            return;

        var text = Format(message, args);
        if (ex is not null)
            text = text + ": " + ex.Message;

        LogManager.Publish(level, text);
    }

    /// <summary>Replaces successive <c>{}</c> placeholders with the given arguments (log4j/SLF4J style).</summary>
    internal static string Format(string message, object?[] args)
    {
        if (args.Length == 0 || !message.Contains("{}", StringComparison.Ordinal))
            return message;

        var sb = new System.Text.StringBuilder(message.Length + 16);
        int argIndex = 0;
        int i = 0;
        while (i < message.Length)
        {
            if (i + 1 < message.Length && message[i] == '{' && message[i + 1] == '}')
            {
                sb.Append(argIndex < args.Length ? Stringify(args[argIndex++]) : "{}");
                i += 2;
            }
            else
            {
                sb.Append(message[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string Stringify(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}

/// <summary>
/// Replacement for log4j's <c>LogManager</c>. Hands out a shared logger and routes
/// every message to <see cref="Sink"/>, which the host wires up to its console.
/// </summary>
public static class LogManager
{
    private static readonly Logger Shared = new();

    /// <summary>Receives every emitted log line. Null = messages are dropped.</summary>
    public static event Action<LogLevel, string>? Sink;

    /// <summary>When false, <c>Debug</c> messages are suppressed.</summary>
    public static bool DebugEnabled { get; set; }

    public static Logger GetLogger() => Shared;

    internal static void Publish(LogLevel level, string text) => Sink?.Invoke(level, text);
}
