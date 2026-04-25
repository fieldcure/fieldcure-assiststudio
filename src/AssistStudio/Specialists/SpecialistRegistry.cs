using System.Diagnostics.CodeAnalysis;
using FieldCure.AssistStudio.Core;

namespace AssistStudio.Specialists;

/// <summary>
/// Registry of built-in specialists. Provides lookup by name.
/// Singleton instance shared across the application.
/// </summary>
public sealed class SpecialistRegistry
{
    /// <summary>
    /// Global singleton instance.
    /// </summary>
    public static SpecialistRegistry Instance { get; } = new();

    private readonly Dictionary<string, ISpecialist> _specialists = new(StringComparer.OrdinalIgnoreCase);

    private SpecialistRegistry()
    {
        Register(new WebSearchSpecialist());
        Register(new CritiqueSpecialist());
        Register(new RedTeamSpecialist());
        Register(new DevilsAdvocateSpecialist());
    }

    /// <summary>
    /// Registers a specialist.
    /// </summary>
    public void Register(ISpecialist specialist)
        => _specialists[specialist.Name] = specialist;

    /// <summary>
    /// Tries to find a specialist by name.
    /// </summary>
    public bool TryGet(string name, [NotNullWhen(true)] out ISpecialist? specialist)
        => _specialists.TryGetValue(name, out specialist);

    /// <summary>
    /// Returns all registered specialists.
    /// </summary>
    public IEnumerable<ISpecialist> GetAll() => _specialists.Values;
}
