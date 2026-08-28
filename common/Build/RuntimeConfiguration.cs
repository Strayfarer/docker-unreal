using System;
using System.IO;

namespace Unreal;

sealed record GitCredentials(string Username, string Password);

sealed record RuntimeConfiguration(
    UnrealVersion Version,
    string Source,
    GitCredentials? Credentials,
    string SourcesRoot,
    string BinariesRoot
) {
    public const string DEFAULT_SOURCE = "https://github.com/EpicGames/UnrealEngine";

    public string SourceDirectory => Path.Combine(SourcesRoot, "EpicGames.UnrealEngine");

    public static RuntimeConfiguration FromEnvironment() {
        var version = UnrealVersion.Parse(EnvironmentVariableNames.UNREAL_VERSION, Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_VERSION));
        string source = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_SOURCE) ?? DEFAULT_SOURCE;
        if (string.IsNullOrWhiteSpace(source)) {
            throw new InvalidOperationException(EnvironmentVariableNames.UNREAL_SOURCE + " cannot be empty");
        }

        string? username = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        string? password = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        if (string.IsNullOrEmpty(username) != string.IsNullOrEmpty(password)) {
            throw new InvalidOperationException(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR + " and " + EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW + " must either both be set or both be omitted");
        }

        var credentials = string.IsNullOrEmpty(username) ? null : new GitCredentials(username, password!);
        return new RuntimeConfiguration(version, source.TrimEnd('/', '\\'), credentials, @"C:\unreal\sources", @"C:\unreal\binaries");
    }
}
