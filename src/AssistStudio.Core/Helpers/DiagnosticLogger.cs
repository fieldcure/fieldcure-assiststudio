using System.Diagnostics;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Lightweight diagnostic logger that delegates to a consumer-provided callback.
/// Library code calls <see cref="LogException"/> or <see cref="LogWarning"/>; the hosting
/// application wires up real logging via <see cref="OnException"/> and <see cref="OnWarning"/>.
/// When no callback is registered, messages are written to <see cref="Debug.WriteLine(string)"/>.
/// </summary>
public static class DiagnosticLogger
{
    /// <summary>
    /// Callback invoked when an exception is logged. Set this at app startup to route
    /// exceptions to your logging infrastructure.
    /// </summary>
    public static Action<Exception>? OnException { get; set; }

    /// <summary>
    /// Callback invoked when a warning message is logged.
    /// </summary>
    public static Action<string>? OnWarning { get; set; }

    /// <summary>
    /// Logs an exception. Invokes <see cref="OnException"/> if registered,
    /// otherwise falls back to <see cref="Debug.WriteLine(string)"/>.
    /// </summary>
    public static void LogException(Exception ex)
    {
        if (OnException is not null)
            OnException(ex);
        else
            Debug.WriteLine(ex);
    }

    /// <summary>
    /// Logs a warning message. Invokes <see cref="OnWarning"/> if registered,
    /// otherwise falls back to <see cref="Debug.WriteLine(string)"/>.
    /// </summary>
    public static void LogWarning(string message)
    {
        if (OnWarning is not null)
            OnWarning(message);
        else
            Debug.WriteLine($"[Warning] {message}");
    }
}
