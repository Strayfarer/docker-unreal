namespace Unreal;

sealed record InstallationMarker(string Version, string PatchVersion, string Source, string Commit);
