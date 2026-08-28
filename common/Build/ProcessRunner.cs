using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Unreal;

sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

static class ProcessRunner {
    public static int Run(string executable, IEnumerable<string> arguments, string workingDirectory, bool requireSuccess, bool removeCredentials = false) {
        var start = CreateStartInfo(executable, arguments, workingDirectory);
        if (removeCredentials) {
            RemoveCredentials(start);
        }
        return Run(start, requireSuccess);
    }

    public static int Run(ProcessStartInfo start, bool requireSuccess) {
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("failed to start " + start.FileName);
        process.WaitForExit();
        if (requireSuccess && process.ExitCode != 0) {
            throw new InvalidOperationException(Path.GetFileName(start.FileName) + " exited with code " + process.ExitCode);
        }

        return process.ExitCode;
    }

    public static int RunBatch(string batchFile, IEnumerable<string> arguments, string workingDirectory, bool requireSuccess, bool removeCredentials = false) {
        var start = CreateBatchStartInfo(batchFile, arguments, workingDirectory);
        if (removeCredentials) {
            RemoveCredentials(start);
        }

        return RunBatch(start, batchFile, requireSuccess);
    }

    public static int RunBatchWithEnvironment(string batchFile, IEnumerable<string> arguments, string workingDirectory, bool requireSuccess, IReadOnlyDictionary<string, string> environment, bool removeCredentials = false) {
        var start = CreateBatchStartInfo(batchFile, arguments, workingDirectory);
        foreach (var variable in environment) {
            start.Environment[variable.Key] = variable.Value;
        }
        if (removeCredentials) {
            RemoveCredentials(start);
        }

        return RunBatch(start, batchFile, requireSuccess);
    }

    static int RunBatch(ProcessStartInfo start, string batchFile, bool requireSuccess) {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("failed to start " + batchFile);
        process.WaitForExit();
        if (requireSuccess && process.ExitCode != 0) {
            throw new InvalidOperationException(Path.GetFileName(batchFile) + " exited with code " + process.ExitCode);
        }

        return process.ExitCode;
    }

    public static ProcessResult Capture(ProcessStartInfo start, bool requireSuccess) {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("failed to start " + start.FileName);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);
        var result = new ProcessResult(process.ExitCode, standardOutput.Result, standardError.Result);
        if (requireSuccess && result.ExitCode != 0) {
            if (!string.IsNullOrWhiteSpace(result.StandardError)) {
                Console.Error.Write(result.StandardError);
            }
            throw new InvalidOperationException(Path.GetFileName(start.FileName) + " exited with code " + result.ExitCode);
        }

        return result;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments, string workingDirectory) {
        var start = new ProcessStartInfo {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (string argument in arguments) {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    internal static ProcessStartInfo CreateBatchStartInfo(string batchFile, IEnumerable<string> arguments, string workingDirectory) {
        var start = CreateStartInfo("cmd.exe", ["/D", "/S", "/C", "call", batchFile], workingDirectory);
        foreach (string argument in arguments) {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    static void RemoveCredentials(ProcessStartInfo start) {
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        start.Environment.Remove(EnvironmentVariableNames.DOCKER_UNREAL_ASKPASS);
        start.Environment.Remove("GIT_ASKPASS");
    }
}
