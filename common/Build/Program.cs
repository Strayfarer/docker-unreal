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
            var command = UnrealCommand.Parse(arguments);
            if (command.Name == EUnrealCommand.Help) {
                Console.Out.WriteLine(UnrealCommand.HELP);
                return 0;
            }

            var configuration = RuntimeConfiguration.FromEnvironment();
            var repository = new GitRepository(configuration.Credentials);
            if (command.Name == EUnrealCommand.Version) {
                var resolution = repository.Resolve(configuration.Source, configuration.Version, configuration.VersionMode);
                Console.Out.WriteLine(resolution.Identifier);
                return 0;
            }

            var cache = new RuntimeCache(configuration.CacheRoot, configuration.Version);
            var store = new InstallationStore(configuration.BinariesRoot);
            using var manifestInstaller = new DependencyManifestInstaller(configuration.Credentials);
            var compiler = new EngineCompiler(manifestInstaller, cache);
            var setup = new RuntimeSetup(configuration, repository, compiler, store, new ToolchainConfigurator());
            var engine = setup.Prepare();
            if (command.Name == EUnrealCommand.Compile) {
                return 0;
            }

            var tool = command.Resolve(engine);
            var environment = InstalledEngineEnvironment.Create(engine, cache.Environment);
            string workingDirectory = Path.GetDirectoryName(tool.Executable)!;
            return tool.IsBatch
                ? ProcessRunner.RunBatchWithEnvironment(tool.Executable, command.Arguments, workingDirectory, false, environment, true)
                : ProcessRunner.RunWithEnvironment(tool.Executable, command.Arguments, workingDirectory, false, environment, true);
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
