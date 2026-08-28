using System;
using System.IO;
using System.Threading;

namespace Unreal;

sealed class InstallationLock : IDisposable {
    readonly FileStream _stream;

    InstallationLock(FileStream stream) => _stream = stream;

    public static InstallationLock Acquire(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var nextNotice = DateTime.UtcNow;
        while (true) {
            try {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new InstallationLock(stream);
            } catch (IOException) when (DateTime.UtcNow >= nextNotice) {
                Console.Out.WriteLine("docker-unreal: waiting for another Unreal Engine installation to finish");
                nextNotice = DateTime.UtcNow.AddMinutes(1);
            } catch (IOException) {
                // The shared installation is still locked by another container.
            }
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    public void Dispose() => _stream.Dispose();
}
