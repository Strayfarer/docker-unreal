using System;
using System.IO;

namespace Unreal;

static class Program {
    static int Main(string[] arguments) {
        if (Environment.GetEnvironmentVariable(EnvironmentVariableNames.DOCKER_UNREAL_ASKPASS) == "1") {
            return AnswerCredentialPrompt(arguments);
        }

        if (arguments.Length == 1 && string.Equals(arguments[0], "--shim-version", StringComparison.OrdinalIgnoreCase)) {
            Console.Out.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
            return 0;
        }

        try {
            var configuration = RuntimeConfiguration.FromEnvironment();
            var cache = new RuntimeCache(configuration.CacheRoot, configuration.Version);
            var repository = new GitRepository(configuration.Credentials);
            var store = new InstallationStore(configuration.BinariesRoot);
            using var manifestInstaller = new DependencyManifestInstaller(configuration.Credentials);
            var compiler = new EngineCompiler(manifestInstaller, cache);
            var setup = new RuntimeSetup(configuration, repository, compiler, store, new ToolchainConfigurator());
            string buildBatch = setup.Prepare();
            return ProcessRunner.RunBatchWithEnvironment(buildBatch, arguments, Path.GetDirectoryName(buildBatch)!, false, cache.Environment, true);
        } catch (Exception exception) {
            Console.Error.WriteLine("docker-unreal: " + exception.Message);
            return 1;
        }
    }

    static int AnswerCredentialPrompt(string[] arguments) {
        string prompt = arguments.Length == 0 ? string.Empty : arguments[0];
        string variable = prompt.Contains("username", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableNames.UNREAL_CREDENTIALS_USR
            : EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW;
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(value)) {
            return 1;
        }

        Console.Out.WriteLine(value);
        return 0;
    }
}
