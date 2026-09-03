using System;

namespace Unreal;

enum EUnrealVersionMode {
    Tag,
    Branch
}

static class UnrealVersionMode {
    public static EUnrealVersionMode Parse(string name, string? value) {
        if (value is null) {
            return EUnrealVersionMode.Tag;
        }
        if (value.Equals("tag", StringComparison.OrdinalIgnoreCase)) {
            return EUnrealVersionMode.Tag;
        }
        if (value.Equals("branch", StringComparison.OrdinalIgnoreCase)) {
            return EUnrealVersionMode.Branch;
        }

        throw new InvalidOperationException(name + " must be tag or branch, got: " + value);
    }
}
