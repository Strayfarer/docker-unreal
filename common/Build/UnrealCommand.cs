using System;
using System.Collections.Generic;
using System.IO;

namespace Unreal;

enum EUnrealCommand {
    Build,
    RunUAT,
    Cmd,
    Version,
    Compile,
    Help
}

sealed record EngineTool(string Executable, bool IsBatch);

sealed record UnrealCommand(EUnrealCommand Name, string[] Arguments) {
    public const string HELP = """
                               Usage:
                                 Unreal Build <Build.bat arguments>
                                 Unreal RunUAT <RunUAT.bat arguments>
                                 Unreal Cmd <UnrealEditor-Cmd.exe arguments>
                                 Unreal --version
                                 Unreal --compile
                                 Unreal --help
                               """;

    public static UnrealCommand Parse(string[] arguments) {
        if (arguments.Length == 0) {
            throw new InvalidOperationException("an Unreal command is required; run 'Unreal --help' for usage");
        }

        string selector = arguments[0];
        string[] toolArguments = RepairSplitOptionExtensions(arguments[1..]);
        if (selector.Equals("Build", StringComparison.OrdinalIgnoreCase)) {
            return new UnrealCommand(EUnrealCommand.Build, toolArguments);
        }
        if (selector.Equals("RunUAT", StringComparison.OrdinalIgnoreCase)) {
            return new UnrealCommand(EUnrealCommand.RunUAT, toolArguments);
        }
        if (selector.Equals("Cmd", StringComparison.OrdinalIgnoreCase)) {
            return new UnrealCommand(EUnrealCommand.Cmd, toolArguments);
        }
        if (selector.Equals("--version", StringComparison.OrdinalIgnoreCase) && arguments.Length == 1) {
            return new UnrealCommand(EUnrealCommand.Version, []);
        }
        if (selector.Equals("--compile", StringComparison.OrdinalIgnoreCase) && arguments.Length == 1) {
            return new UnrealCommand(EUnrealCommand.Compile, []);
        }
        if (selector.Equals("--help", StringComparison.OrdinalIgnoreCase) && arguments.Length == 1) {
            return new UnrealCommand(EUnrealCommand.Help, []);
        }

        throw new InvalidOperationException("unknown Unreal command: " + selector + "; run 'Unreal --help' for usage");
    }

    static string[] RepairSplitOptionExtensions(string[] arguments) {
        // Windows Docker exec can split a dotted -key=value before ".ext", either within or between argv values.
        var repaired = new List<string>(arguments.Length);
        foreach (string argument in arguments) {
            string current = RepairEmbeddedOptionExtension(argument);
            if (IsExtensionFragment(current)
                && repaired.Count > 0
                && repaired[^1].StartsWith("-", StringComparison.Ordinal)
                && repaired[^1].Contains("=", StringComparison.Ordinal)) {
                repaired[^1] += current;
            } else {
                repaired.Add(current);
            }
        }

        return repaired.ToArray();
    }

    static string RepairEmbeddedOptionExtension(string argument) {
        if (!argument.StartsWith("-", StringComparison.Ordinal)) {
            return argument;
        }

        int equals = argument.IndexOf('=');
        int separator = argument.LastIndexOf(" .", StringComparison.Ordinal);
        if (equals < 0 || separator < equals) {
            return argument;
        }

        string extension = argument[(separator + 1)..];
        return IsExtensionFragment(extension) ? argument.Remove(separator, 1) : argument;
    }

    static bool IsExtensionFragment(string argument) {
        if (argument.Length < 2 || argument[0] != '.') {
            return false;
        }

        for (int index = 1; index < argument.Length; index++) {
            char character = argument[index];
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '-') {
                return false;
            }
        }

        return true;
    }

    public EngineTool Resolve(InstalledEngine engine) => Name switch {
        EUnrealCommand.Build => new EngineTool(
            Path.Combine(engine.Root, "Engine", "Build", "BatchFiles", "Build.bat"),
            true
        ),
        EUnrealCommand.RunUAT => new EngineTool(
            Path.Combine(engine.Root, "Engine", "Build", "BatchFiles", "RunUAT.bat"),
            true
        ),
        EUnrealCommand.Cmd => new EngineTool(
            Path.Combine(engine.Root, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe"),
            false
        ),
        _ => throw new InvalidOperationException("the Unreal command does not select an engine tool: " + Name)
    };
}
