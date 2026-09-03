using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Unreal;

sealed class SemanticVersion : IComparable<SemanticVersion> {
    public BigInteger Major { get; }
    public BigInteger Minor { get; }
    public BigInteger Patch { get; }
    public IReadOnlyList<string> Prerelease { get; }

    SemanticVersion(BigInteger major, BigInteger minor, BigInteger patch, IReadOnlyList<string> prerelease) {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public static bool TryParse(string value, out SemanticVersion? version) {
        version = null;
        int buildSeparator = value.IndexOf('+');
        if (buildSeparator >= 0) {
            if (value.IndexOf('+', buildSeparator + 1) >= 0
                || !ValidIdentifiers(value[(buildSeparator + 1)..], false)) {
                return false;
            }
            value = value[..buildSeparator];
        }

        string prereleaseValue = string.Empty;
        int prereleaseSeparator = value.IndexOf('-');
        if (prereleaseSeparator >= 0) {
            prereleaseValue = value[(prereleaseSeparator + 1)..];
            value = value[..prereleaseSeparator];
            if (!ValidIdentifiers(prereleaseValue, true)) {
                return false;
            }
        }

        string[] core = value.Split('.');
        if (core.Length != 3
            || !TryParseNumber(core[0], out var major)
            || !TryParseNumber(core[1], out var minor)
            || !TryParseNumber(core[2], out var patch)) {
            return false;
        }

        string[] prerelease = prereleaseValue.Length == 0 ? [] : prereleaseValue.Split('.');
        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other) {
        if (other is null) {
            return 1;
        }

        int comparison = Major.CompareTo(other.Major);
        if (comparison != 0) {
            return comparison;
        }
        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0) {
            return comparison;
        }
        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) {
            return comparison;
        }
        if (Prerelease.Count == 0 || other.Prerelease.Count == 0) {
            return Prerelease.Count == other.Prerelease.Count ? 0 : Prerelease.Count == 0 ? 1 : -1;
        }

        int sharedIdentifiers = Math.Min(Prerelease.Count, other.Prerelease.Count);
        for (int index = 0; index < sharedIdentifiers; index++) {
            string left = Prerelease[index];
            string right = other.Prerelease[index];
            bool leftNumeric = IsNumeric(left);
            bool rightNumeric = IsNumeric(right);
            if (leftNumeric && rightNumeric) {
                comparison = BigInteger.Parse(left, CultureInfo.InvariantCulture)
                    .CompareTo(BigInteger.Parse(right, CultureInfo.InvariantCulture));
            } else if (leftNumeric != rightNumeric) {
                comparison = leftNumeric ? -1 : 1;
            } else {
                comparison = string.CompareOrdinal(left, right);
            }
            if (comparison != 0) {
                return comparison;
            }
        }

        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    static bool TryParseNumber(string value, out BigInteger number) {
        number = BigInteger.Zero;
        return IsNumeric(value)
               && (value.Length == 1 || value[0] != '0')
               && BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    static bool ValidIdentifiers(string value, bool rejectLeadingZeroes) {
        string[] identifiers = value.Split('.');
        foreach (string identifier in identifiers) {
            if (identifier.Length == 0) {
                return false;
            }
            foreach (char character in identifier) {
                if (!IsAsciiLetterOrDigit(character) && character != '-') {
                    return false;
                }
            }
            if (rejectLeadingZeroes && IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0') {
                return false;
            }
        }

        return true;
    }

    static bool IsNumeric(string value) {
        if (value.Length == 0) {
            return false;
        }
        foreach (char character in value) {
            if (character is < '0' or > '9') {
                return false;
            }
        }

        return true;
    }

    static bool IsAsciiLetterOrDigit(char character) =>
        (character >= '0' && character <= '9')
        || (character >= 'A' && character <= 'Z')
        || (character >= 'a' && character <= 'z');
}
