using FieldCure.AssistStudio.Core;

namespace AssistStudio.Specialists;

/// <summary>
/// Built-in web search specialist. Searches the web using Essentials'
/// web_search/web_fetch tools and returns a structured research report.
/// </summary>
public sealed class WebSearchSpecialist : ISpecialist
{
    /// <inheritdoc />
    public string Name => "web_search_specialist";

    /// <inheritdoc />
    public string DisplayName => "Web Search Specialist";

    /// <inheritdoc />
    public IReadOnlyList<string> AllowedTools { get; } =
    [
        // Essentials built-in
        "web_search", "web_fetch",
        // Serper / SerpApi / Tavily MCP servers
        "google_search", "googlesearch", "google_search_tool", "scrape",
        // Brave Search MCP
        "brave_search",
        // Common utility
        "run_javascript",
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> FallbackServers { get; } = ["builtin_essentials"];

    /// <inheritdoc />
    public int MaxRounds => 15;

    /// <inheritdoc />
    public TimeSpan Timeout => TimeSpan.FromMinutes(2);

    /// <summary>
    /// Routing guideline injected into the parent conversation's system prompt
    /// when the specialist is enabled. Guides the AI on when to search directly
    /// vs. delegate to the specialist.
    /// </summary>
    public const string RoutingGuideline =
        """
        ## Web Search & Specialists

        You have two options for web information:

        - **Direct search**: Use `web_search` tool directly for quick factual lookups
          (e.g. "current price of X", "who is the CEO of Y").
          Fast, lightweight, stays in this conversation.

        - **Web Search Specialist**: Delegate to `web_search_specialist` via `delegate_task` for
          in-depth research requiring multiple sources
          (e.g. "analyze recent trends in X", "compare A vs B with latest data").
          Use: `delegate_task(prompt: "...", specialist: "web_search_specialist")`
          Takes longer but returns a structured report with sources.

        **Do NOT search** when:
        - The question is about general knowledge you already know well.
        - The user is asking for opinions, creative writing, or code.
        - The user explicitly says not to search.

        **Always search** when:
        - The user asks about current events, prices, or recent developments.
        - The user explicitly asks you to search or look something up.
        - You are uncertain about facts that could have changed recently.
        """;

    /// <inheritdoc />
    public string BuildSystemPrompt(string userQuery, IReadOnlyDictionary<string, string>? contextHints = null)
    {
        var prompt =
            $"""
            You are a web research specialist. Your job is to search the web,
            read relevant pages, and produce a concise research report.

            ## Rules

            1. **Query strategy**
               - If the topic is in Korean or about Korea, search BOTH in Korean
                 (region: "ko-kr") AND in English for broader coverage.
               - For academic/medical topics, prefer English queries.
               - If initial results are insufficient, rephrase and retry (max 2 retries).

            2. **Source selection**
               - From search results, pick the 2-3 most relevant URLs.
               - Prefer primary sources (official sites, papers, docs) over aggregators.
               - Skip forums, SEO spam, and low-quality sites.

            3. **Reading**
               - Fetch each selected page (max_length 8000) and read the content.
               - If a page is too noisy, use a script to extract specific sections.

            4. **Report format**
               Return your report in this exact structure:

               ## 요약
               [핵심 발견 2-3문장]

               ## 출처
               1. [제목](url) — 핵심 내용 한 줄
               2. [제목](url) — 핵심 내용 한 줄

               ## 상세
               [분석 내용. 출처별로 정리.]

            5. **Language**
               - Write the report in the same language as the user's query.

            ## User query
            {userQuery}
            """;

        if (contextHints is { Count: > 0 })
        {
            prompt += "\n\n## Context";
            foreach (var (key, value) in contextHints)
                prompt += $"\n- {key}: {value}";
        }

        return prompt;
    }
}
