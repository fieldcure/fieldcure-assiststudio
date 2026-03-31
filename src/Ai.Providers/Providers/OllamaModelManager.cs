using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers;

/// <summary>
/// Manages local Ollama models, including listing, searching, downloading, and deleting.
/// </summary>
public partial class OllamaModelManager : IModelManager, IDisposable
{
    #region Fields

    /// <summary>The HTTP client used for API requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The base URL of the Ollama server.</summary>
    private readonly string _baseUrl;

    /// <summary>Whether this instance owns (and should dispose) the HTTP client.</summary>
    private readonly bool _ownsHttpClient;

    #endregion

    #region Constants

    /// <summary>A curated list of recommended Ollama models for search results.</summary>
    private static readonly IReadOnlyList<(string Name, string DisplayName, string Family)> RecommendedModels =
    [
        ("llama3.1", "Meta Llama 3.1 (8B)", "llama"),
        ("llama3.1:70b", "Meta Llama 3.1 (70B)", "llama"),
        ("phi4", "Microsoft Phi-4 (14B)", "phi"),
        ("gemma2", "Google Gemma 2 (9B)", "gemma"),
        ("qwen2.5", "Alibaba Qwen 2.5 (7B)", "qwen"),
        ("mistral", "Mistral 7B", "mistral"),
        ("deepseek-r1", "DeepSeek R1", "deepseek"),
        ("codellama", "Code Llama", "llama"),
        ("llava", "LLaVA (Vision)", "llava"),
    ];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="OllamaModelManager"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaModelManager(string baseUrl = "http://localhost:11434")
        : this(new HttpClient(), baseUrl, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="OllamaModelManager"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaModelManager(HttpClient httpClient, string baseUrl = "http://localhost:11434")
        : this(httpClient, baseUrl, ownsHttpClient: false)
    {
    }

    /// <summary>
    /// Internal constructor that captures all dependencies.
    /// </summary>
    private OllamaModelManager(HttpClient httpClient, string baseUrl, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        _baseUrl = baseUrl.TrimEnd('/');
        _ownsHttpClient = ownsHttpClient;
    }

    #endregion

    #region IModelManager Implementation

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LocalModel>> ListLocalModelsAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
        var response = await _httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<LocalModel>();
        foreach (var item in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var size = item.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
            var modifiedAt = item.TryGetProperty("modified_at", out var modEl)
                ? (DateTime?)DateTime.Parse(modEl.GetString()!)
                : null;

            string? family = null, paramSize = null, quantLevel = null;
            if (item.TryGetProperty("details", out var details))
            {
                family = details.TryGetProperty("family", out var fEl) ? fEl.GetString() : null;
                paramSize = details.TryGetProperty("parameter_size", out var pEl) ? pEl.GetString() : null;
                quantLevel = details.TryGetProperty("quantization_level", out var qEl) ? qEl.GetString() : null;
            }

            models.Add(new LocalModel(name, name, "ollama")
            {
                SizeBytes = size,
                Family = family,
                ParameterSize = paramSize,
                QuantizationLevel = quantLevel,
                ModifiedAt = modifiedAt,
                IsDownloaded = true
            });
        }

        return models;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LocalModel>> SearchAvailableModelsAsync(
        string? query = null, CancellationToken ct = default)
    {
        var results = RecommendedModels
            .Where(m => string.IsNullOrEmpty(query) ||
                        m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(m => new LocalModel(m.Name, m.DisplayName, "ollama")
            {
                Family = m.Family,
                IsDownloaded = false
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<LocalModel>>(results);
    }

    /// <inheritdoc/>
    public async Task DownloadModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject { ["name"] = modelName, ["stream"] = true };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/pull")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var lastReport = DateTime.MinValue;
        ModelDownloadProgress? pending = null;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var sEl) ? sEl.GetString() ?? "" : "";
            var total = root.TryGetProperty("total", out var tEl) ? (long?)tEl.GetInt64() : null;
            var completed = root.TryGetProperty("completed", out var cEl) ? (long?)cEl.GetInt64() : null;

            var percent = total > 0 && completed.HasValue
                ? (double)completed.Value / total.Value
                : 0.0;

            var p = new ModelDownloadProgress(status, percent, total, completed);
            var now = DateTime.UtcNow;

            // Throttle progress reports to avoid flooding the UI dispatcher
            if ((now - lastReport).TotalMilliseconds >= 200 || percent >= 1.0)
            {
                progress?.Report(p);
                lastReport = now;
                pending = null;
            }
            else
            {
                pending = p;
            }
        }

        // Report any remaining progress
        if (pending is not null)
            progress?.Report(pending);
    }

    /// <inheritdoc/>
    public async Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        var body = new JsonObject { ["name"] = modelName };
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/api/delete")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    #endregion
}
