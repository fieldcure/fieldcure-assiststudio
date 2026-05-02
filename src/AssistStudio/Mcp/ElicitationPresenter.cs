using FieldCure.AssistStudio.Controls;
using ModelContextProtocol.Protocol;

namespace AssistStudio.Mcp;

/// <summary>
/// Display-ready MCP elicitation request passed to a presenter.
/// </summary>
internal sealed record ElicitationRequest(
    string ToolName,
    string ServerName,
    string Message,
    IReadOnlyList<ElicitationFieldInfo> Fields);

/// <summary>
/// Renders an elicitation request to the user and returns their response.
/// Implementations decide the host (inline panel, modal dialog, etc.).
/// </summary>
internal interface IElicitationPresenter
{
    /// <summary>Presents the request and returns the MCP protocol result.</summary>
    Task<ElicitResult> PresentAsync(
        ElicitationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Scope returned when a temporary elicitation presenter is pushed onto a connection.
/// Disposing restores the previous presenter.
/// </summary>
internal sealed class ElicitationPresenterScope : IDisposable
{
    private readonly McpServerConnection _connection;
    private bool _disposed;

    internal ElicitationPresenterScope(
        McpServerConnection connection,
        IElicitationPresenter presenter)
    {
        _connection = connection;
        Presenter = presenter;
    }

    /// <summary>Gets the presenter active for this scope.</summary>
    internal IElicitationPresenter Presenter { get; }

    /// <summary>Gets whether any elicitation in this scope returned cancel or decline.</summary>
    public bool WasCancelled { get; private set; }

    /// <summary>Marks this scope as cancelled by the user.</summary>
    internal void MarkCancelled() => WasCancelled = true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connection.PopElicitationPresenter(this);
    }
}
