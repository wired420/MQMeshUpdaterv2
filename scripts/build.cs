// Tests the solution, publishes the Windows and Linux apps, and packages
// both into dist/. Run it through build.ps1 or build.sh at the repo root.
//
//   dotnet run scripts/build.cs -- [--skip-tests] [--out <folder>]

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

var root = AppContext.GetData("EntryPointFileDirectoryPath") is string scripts
    ? Path.GetFullPath(Path.Combine(scripts, ".."))
    : Directory.GetCurrentDirectory();
var skipTests = args.Contains("--skip-tests");
var outIndex = Array.IndexOf(args, "--out");
var dist = outIndex >= 0 && outIndex + 1 < args.Length ? Path.GetFullPath(args[outIndex + 1]) : Path.Combine(root, "dist");
var staging = Path.Combine(root, "artifacts", "publish");

var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
var version = Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;

Console.WriteLine($"MQMesh Updater {version}: building into {dist}");

if (!skipTests)
{
    Run("dotnet", "test", "--configuration", "Release");
}

Reset(staging);
Run("dotnet", "publish", "src/MQMeshUpdater.Linux", "-c", "Release", "-r", "linux-x64", "-o", Path.Combine(staging, "linux-x64"));

var windowsArgs = new List<string> { "publish", "src/MQMeshUpdater", "-c", "Release", "-r", "win-x64", "-o", Path.Combine(staging, "win-x64") };

if (!OperatingSystem.IsWindows())
{
    // Only Windows can write the icon and manifest into the exe; the result still runs.
    Console.WriteLine("Note: not on Windows, so MQMeshUpdater.exe gets no icon or manifest. Build on Windows for a release.");
    windowsArgs.Add("-p:NoWarn=NETSDK1074");
}

Run("dotnet", [.. windowsArgs]);

Reset(dist);

var exe = Path.Combine(staging, "win-x64", "MQMeshUpdater.exe");
var linux = Path.Combine(staging, "linux-x64", "mqmesh-updater");
var windowsName = $"MQMeshUpdater-{version}-win-x64";
var linuxName = $"mqmesh-updater-{version}-linux-x64";

File.Copy(exe, Path.Combine(dist, windowsName + ".exe"));

using (var zip = ZipFile.Open(Path.Combine(dist, windowsName + ".zip"), ZipArchiveMode.Create))
{
    zip.CreateEntryFromFile(exe, "MQMeshUpdater.exe", CompressionLevel.Optimal);
    zip.CreateEntryFromFile(Path.Combine(root, "packaging", "README-windows.txt"), "README.txt", CompressionLevel.Optimal);
}

var linuxCopy = Path.Combine(dist, linuxName);
File.Copy(linux, linuxCopy);

if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(linuxCopy, (UnixFileMode)0b111_101_101);
}

// Written by hand so the binary is executable even when packed on Windows.
await using (var file = File.Create(Path.Combine(dist, linuxName + ".tar.gz")))
await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
{
    await AddAsync(tar, linux, "mqmesh-updater", (UnixFileMode)0b111_101_101);
    await AddAsync(tar, Path.Combine(root, "packaging", "README-linux.txt"), "README.txt", (UnixFileMode)0b110_100_100);
}

var sums = Directory.GetFiles(dist).Order(StringComparer.Ordinal)
    .Select(f => $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))}  {Path.GetFileName(f)}\n");

// Unix line endings, so sha256sum --check reads it on Linux.
File.WriteAllText(Path.Combine(dist, "SHA256SUMS.txt"), string.Concat(sums));

Console.WriteLine();

foreach (var file in Directory.GetFiles(dist).Order(StringComparer.Ordinal))
{
    Console.WriteLine($"{new FileInfo(file).Length / 1024.0 / 1024.0,8:0.0} MB  {Path.GetFileName(file)}");
}

return 0;

void Run(string command, params string[] arguments)
{
    Console.WriteLine($"> {command} {string.Join(' ', arguments)}");
    var start = new ProcessStartInfo(command) { WorkingDirectory = root };

    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start)!;
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine($"{command} failed with exit code {process.ExitCode}.");
        Environment.Exit(process.ExitCode);
    }
}

static void Reset(string folder)
{
    if (Directory.Exists(folder))
    {
        Directory.Delete(folder, recursive: true);
    }

    Directory.CreateDirectory(folder);
}

static async Task AddAsync(TarWriter tar, string path, string name, UnixFileMode mode)
{
    var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
    {
        Mode = mode,
        ModificationTime = File.GetLastWriteTimeUtc(path),
        DataStream = File.OpenRead(path),
    };

    await using (entry.DataStream)
    {
        await tar.WriteEntryAsync(entry);
    }
}
