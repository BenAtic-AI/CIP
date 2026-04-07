using Cip.Contracts.Constants;
using Cip.Domain.Documents;

namespace Integrations.AzureAi;

internal static class ProfileCardMarkdown
{
    private const int MaxSummaryItems = 3;
    private const int MaxSynopsisLength = 220;

    public static string CreateDeterministic(ProfileDocument profile)
    {
        var lines = new[]
        {
            "### Profile summary",
            $"- **Status:** {FormatStatus(profile.Status)}",
            $"- **Identities:** {FormatIdentities(profile.Identities)}",
            $"- **Traits:** {FormatTraits(profile.Traits)}",
            $"- **Synopsis:** {Trim(profile.Synopsis, MaxSynopsisLength)}",
            "> AI-generated from stable profile fields; verify against source evidence."
        };

        return Normalize(string.Join('\n', lines));
    }

    public static string Normalize(string? markdown)
    {
        var normalized = (markdown ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length <= Constants.Profiles.ProfileCardMaxLength)
        {
            return normalized;
        }

        return normalized[..(Constants.Profiles.ProfileCardMaxLength - 1)].TrimEnd() + "…";
    }

    public static string BuildPrompt(ProfileDocument profile)
    {
        var identities = profile.Identities.Count == 0
            ? "- none"
            : string.Join('\n', profile.Identities
                .OrderBy(identity => identity.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(identity => identity.Value, StringComparer.OrdinalIgnoreCase)
                .Select(identity => $"- {identity.Type}: {identity.Value} (source: {identity.Source})"));

        var traits = profile.Traits.Count == 0
            ? "- none"
            : string.Join('\n', profile.Traits
                .OrderBy(trait => trait.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(trait => trait.Value, StringComparer.OrdinalIgnoreCase)
                .Select(trait => $"- {trait.Name} = {trait.Value} (confidence: {trait.Confidence:0.##})"));

        return string.Join('\n',
            "Create a concise Markdown profile card for CIP operators.",
            "Use only the stable fields below and do not invent facts.",
            "Keep the result non-authoritative, useful for prompt conditioning, and under 600 characters.",
            "Return Markdown only.",
            string.Empty,
            $"Status: {FormatStatus(profile.Status)}",
            "Identities:",
            identities,
            "Traits:",
            traits,
            $"Synopsis: {Trim(profile.Synopsis, 400)}",
            string.Empty,
            "Required shape:",
            "### Profile summary",
            "- **Status:** ...",
            "- **Identities:** ...",
            "- **Traits:** ...",
            "- **Synopsis:** ...",
            "> AI-generated from stable profile fields; verify against source evidence.");
    }

    private static string FormatStatus(string status)
        => string.Equals(status, Constants.Profiles.PendingReview, StringComparison.OrdinalIgnoreCase)
            ? "Pending review"
            : string.Equals(status, Constants.Profiles.Ready, StringComparison.OrdinalIgnoreCase)
                ? "Ready"
                : "Unknown";

    private static string FormatIdentities(IReadOnlyCollection<ProfileIdentity> identities)
        => FormatSummary(
            identities
                .Select(identity => $"{identity.Type}:{identity.Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray());

    private static string FormatTraits(IReadOnlyCollection<ProfileTrait> traits)
        => FormatSummary(
            traits
                .Select(trait => $"{trait.Name}={trait.Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray());

    private static string FormatSummary(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "none";
        }

        var visibleValues = values.Take(MaxSummaryItems).ToArray();
        var suffix = values.Count > MaxSummaryItems ? $" (+{values.Count - MaxSummaryItems} more)" : string.Empty;
        return string.Join("; ", visibleValues) + suffix;
    }

    private static string Trim(string? value, int maxLength)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length <= maxLength)
        {
            return string.IsNullOrWhiteSpace(normalized) ? "none" : normalized;
        }

        return normalized[..(maxLength - 1)].TrimEnd() + "…";
    }
}
