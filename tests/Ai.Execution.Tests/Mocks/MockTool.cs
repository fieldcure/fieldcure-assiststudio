using System.Text.Json;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution.Tests.Mocks;

/// <summary>
/// Mock tool that records calls and returns configurable results.
/// </summary>
internal sealed class MockTool : IAssistTool
{
    readonly Func<JsonElement, CancellationToken, Task<string>> _handler;

    public string Name { get; }
    public string Description => $"Mock tool: {Name}";
    public string ParameterSchema => """{"type":"object"}""";

    public List<JsonElement> ReceivedArgs { get; } = [];
    public int CallCount => ReceivedArgs.Count;

    public MockTool(string name, string result = "{}")
        : this(name, (_, _) => Task.FromResult(result)) { }

    public MockTool(string name, Func<JsonElement, CancellationToken, Task<string>> handler)
    {
        Name = name;
        _handler = handler;
    }

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        ReceivedArgs.Add(parameters);
        return await _handler(parameters, ct);
    }

    /// <summary>Creates a tool that always throws.</summary>
    public static MockTool Failing(string name, string errorMessage = "Tool failed")
        => new(name, (_, _) => throw new InvalidOperationException(errorMessage));
}
