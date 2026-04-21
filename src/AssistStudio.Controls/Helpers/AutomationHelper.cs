using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Centralized helpers for applying <see cref="AutomationProperties"/> values
/// (<c>AutomationId</c>, <c>Name</c>, <c>HelpText</c>) from code-behind while
/// keeping <c>Name</c>/<c>HelpText</c> localized through the Controls project's
/// <c>.resw</c> resources.
/// </summary>
/// <remarks>
/// <para>
/// Project rules enforced by this helper (see CLAUDE.md):
/// <list type="bullet">
/// <item><description><c>AutomationId</c> is English PascalCase and is never localized.</description></item>
/// <item><description><c>Name</c> and <c>HelpText</c> are looked up in the Controls project's
/// <c>AssistStudio.Controls/Resources</c> resource map.</description></item>
/// <item><description>Missing resource keys are silently skipped so partially-annotated elements
/// still work rather than crashing at runtime.</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class AutomationHelper
{
    /// <summary>
    /// The default resource loader pointing at the Controls project's
    /// <c>AssistStudio.Controls/Resources</c> subtree.
    /// </summary>
    private static readonly ResourceLoader DefaultLoader =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

    /// <summary>
    /// Applies <see cref="AutomationProperties.AutomationIdProperty"/>,
    /// <see cref="AutomationProperties.NameProperty"/> and
    /// <see cref="AutomationProperties.HelpTextProperty"/> on <paramref name="element"/>
    /// from the given keys. The Name and HelpText are resolved via <paramref name="loader"/>
    /// (falling back to the Controls default loader). Missing resource keys are ignored.
    /// </summary>
    /// <param name="element">The target element (must be a <see cref="DependencyObject"/>).</param>
    /// <param name="automationId">English PascalCase automation identifier. Required.</param>
    /// <param name="nameKey">Optional resw key for <c>AutomationProperties.Name</c>.</param>
    /// <param name="helpTextKey">Optional resw key for <c>AutomationProperties.HelpText</c>.</param>
    /// <param name="loader">Optional custom loader. Falls back to the Controls loader when null.</param>
    public static void SetAutomation(
        DependencyObject element,
        string automationId,
        string? nameKey = null,
        string? helpTextKey = null,
        ResourceLoader? loader = null)
    {
        if (element is null) return;

        if (!string.IsNullOrEmpty(automationId))
        {
            AutomationProperties.SetAutomationId(element, automationId);
        }

        var resolver = loader ?? DefaultLoader;

        if (!string.IsNullOrEmpty(nameKey))
        {
            var name = SafeGetString(resolver, nameKey);
            if (!string.IsNullOrEmpty(name))
            {
                AutomationProperties.SetName(element, name);
            }
        }

        if (!string.IsNullOrEmpty(helpTextKey))
        {
            var help = SafeGetString(resolver, helpTextKey);
            if (!string.IsNullOrEmpty(help))
            {
                AutomationProperties.SetHelpText(element, help);
            }
        }
    }

    /// <summary>
    /// Overload that accepts an already-resolved <paramref name="name"/> value — useful
    /// when the accessible name must include data-bound content (e.g.
    /// <c>"Attachment: report.pdf"</c>). Optional <paramref name="helpText"/> is also
    /// used literally.
    /// </summary>
    /// <param name="element">The target element.</param>
    /// <param name="automationId">English PascalCase automation identifier.</param>
    /// <param name="name">Already-resolved accessible name, or null to skip.</param>
    /// <param name="helpText">Already-resolved help text, or null to skip.</param>
    public static void SetAutomationLiteral(
        DependencyObject element,
        string automationId,
        string? name = null,
        string? helpText = null)
    {
        if (element is null) return;

        if (!string.IsNullOrEmpty(automationId))
        {
            AutomationProperties.SetAutomationId(element, automationId);
        }

        if (!string.IsNullOrEmpty(name))
        {
            AutomationProperties.SetName(element, name);
        }

        if (!string.IsNullOrEmpty(helpText))
        {
            AutomationProperties.SetHelpText(element, helpText);
        }
    }

    /// <summary>
    /// Fetches a localized string, swallowing any exception / missing key and returning
    /// null. Matches the project rule that missing resources should degrade gracefully.
    /// </summary>
    private static string? SafeGetString(ResourceLoader loader, string key)
    {
        try
        {
            var value = loader.GetString(key);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutomationHelper] Missing resource key '{key}': {ex.Message}");
            return null;
        }
    }
}
