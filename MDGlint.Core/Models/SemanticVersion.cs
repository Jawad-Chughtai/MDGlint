using System.Globalization;
using System.Text.RegularExpressions;

namespace MDGlint.Core.Models;

/// <summary>
/// A version as MDGlint uses it: <c>major.minor.patch</c> with an optional pre-release suffix
/// (<c>1.2.0-beta.1</c>), ordered by Semantic Versioning 2.0 rules. Build metadata after <c>+</c>
/// is accepted and ignored. A leading <c>v</c> (as in release tags) is accepted.
/// </summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The dot-separated pre-release identifiers; empty for a stable release.</summary>
    public IReadOnlyList<string> PreRelease { get; }

    public bool IsPreRelease => PreRelease.Count > 0;

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = VersionPattern().Match(text.Trim());
        if (!match.Success
            || !TryParseNumber(match.Groups["major"].Value, out var major)
            || !TryParseNumber(match.Groups["minor"].Value, out var minor)
            || !TryParseNumber(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        var preRelease = match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : [];
        if (preRelease.Any(id => id.Length > 1 && id[0] == '0' && id.All(char.IsAsciiDigit)))
        {
            return false; // Numeric identifiers can't have leading zeros.
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A stable release is newer than any pre-release of the same version.
        if (!IsPreRelease || !other.IsPreRelease)
        {
            return other.IsPreRelease.CompareTo(IsPreRelease);
        }

        for (var i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            var result = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PreRelease));

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{string.Join('.', PreRelease)}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            // Compare by length first so long numbers don't overflow.
            var byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }

        // Numeric identifiers sort before alphanumeric ones.
        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return Math.Sign(string.CompareOrdinal(left, right));
    }

    private static bool TryParseNumber(string text, out int value)
    {
        value = 0;
        if (text.Length > 1 && text[0] == '0')
        {
            return false; // No leading zeros.
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();
}
