using System;
using System.Globalization;

namespace Unreal;

sealed record UnrealVersion(int Major, int Minor) {
    public static UnrealVersion Parse(string name, string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException(name + " is required");
        }

        string[] components = value.Split('.');
        if (components.Length != 2
            || !int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(components[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) {
            throw Invalid(name, value);
        }

        var version = new UnrealVersion(major, minor);
        if (version.ToString() != value || major is < 5 or > 6) {
            throw Invalid(name, value);
        }

        return version;
    }

    public bool Matches(BuildVersion version) => version.MajorVersion == Major && version.MinorVersion == Minor;

    public override string ToString() => Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture);

    static InvalidOperationException Invalid(string name, string value) => new(name + " must be a minor Unreal Engine version from 5.0 through 6.x, got: " + value);
}
