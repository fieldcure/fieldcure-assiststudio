using System.Net.Http;
using System.Net.Sockets;

namespace FieldCure.Ai.Providers.Helpers;

/// <summary>
/// Maps Ollama connection exceptions to categorized error codes with user-friendly messages.
/// </summary>
public static class OllamaErrorHelper
{
    #region Error Codes

    /// <summary>
    /// Error category for Ollama connection failures.
    /// </summary>
    public enum OllamaErrorCode
    {
        /// <summary>The server actively refused the connection.</summary>
        ConnectionRefused,

        /// <summary>The connection attempt timed out.</summary>
        Timeout,

        /// <summary>The host name could not be resolved.</summary>
        HostNotFound,

        /// <summary>The network is unreachable.</summary>
        NetworkUnreachable,

        /// <summary>The server returned a non-success HTTP status code.</summary>
        HttpError,

        /// <summary>An unrecognized error occurred.</summary>
        Unknown
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Categorizes an exception into an <see cref="OllamaErrorCode"/>.
    /// </summary>
    /// <param name="ex">The exception to categorize.</param>
    /// <returns>The matching error code.</returns>
    public static OllamaErrorCode Categorize(Exception ex)
    {
        // Dig out SocketException from HttpRequestException chain
        var socketEx = FindSocketException(ex);
        if (socketEx is not null)
        {
            return socketEx.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => OllamaErrorCode.ConnectionRefused,
                SocketError.HostNotFound => OllamaErrorCode.HostNotFound,
                SocketError.NetworkUnreachable => OllamaErrorCode.NetworkUnreachable,
                SocketError.TimedOut => OllamaErrorCode.Timeout,
                _ => OllamaErrorCode.ConnectionRefused
            };
        }

        return ex switch
        {
            TaskCanceledException or OperationCanceledException => OllamaErrorCode.Timeout,
            HttpRequestException => OllamaErrorCode.ConnectionRefused,
            _ => OllamaErrorCode.Unknown
        };
    }

    /// <summary>
    /// Returns a default English error message for the given code.
    /// Consumers should prefer localized strings keyed by <c>Ollama_Error_{code}</c>.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <returns>A user-friendly error message in English.</returns>
    public static string GetDefaultMessage(OllamaErrorCode code) => code switch
    {
        OllamaErrorCode.ConnectionRefused => "Could not connect to Ollama server. Check that it is running.",
        OllamaErrorCode.Timeout => "Connection timed out. The server may be unreachable.",
        OllamaErrorCode.HostNotFound => "Host not found. Check the server address.",
        OllamaErrorCode.NetworkUnreachable => "Network unreachable. Check your network connection.",
        OllamaErrorCode.HttpError => "Server returned an error.",
        OllamaErrorCode.Unknown => "An unexpected error occurred.",
        _ => "An unexpected error occurred."
    };

    #endregion

    #region Private Methods

    /// <summary>
    /// Walks the exception's InnerException chain to find a <see cref="SocketException"/>.
    /// </summary>
    private static SocketException? FindSocketException(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is SocketException se)
                return se;
            current = current.InnerException;
        }
        return null;
    }

    #endregion
}
