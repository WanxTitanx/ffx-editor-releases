// ============================================================================
// FfxedRuntimeLauncher — strict packaged FFXED/JRE launch boundary
// PURPOSE : verify the bundled FFXED JAR and launch it with the private JRE
//           below runtimes/java using separated arguments and no shell.
// WHY     : product launches must not depend on Java from PATH/JAVA_HOME or on
//           developer-machine overrides, and must reject an unexpected JAR.
// MAINT   : update ExpectedJarSha256 only with reviewed FFXED provenance.
// ============================================================================

using FFXProjectEditor.Diagnostics;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public enum FfxedLaunchFailure
{
    None,
    PrivateJavaRuntimeMissing,
    BundledJarMissing,
    JarHashMismatch,
    ProcessStartFailed,
}

public sealed record FfxedLaunchPreparation(
    FfxedLaunchFailure Failure,
    ProcessStartInfo? StartInfo,
    string? JarSha256,
    string? RuntimeSha256,
    string? Detail);

public sealed record FfxedLaunchResult(
    bool Started,
    FfxedLaunchFailure Failure,
    string? JarSha256,
    string? RuntimeSha256,
    string? Detail);

public interface IFfxedProcessStarter
{
    bool Start(ProcessStartInfo startInfo);
}

public sealed class SystemFfxedProcessStarter : IFfxedProcessStarter
{
    public bool Start(ProcessStartInfo startInfo) => Process.Start(startInfo) != null;
}

public sealed class FfxedRuntimeLauncher
{
    public const string ExpectedJarSha256 =
        "F68CE3D9999631A214F51E72A9FC3786A40B3B35D909CEEA549BF04C4E358D2E";

    private readonly string baseDirectory;
    private readonly BundledJavaRuntimeLocator runtimeLocator;
    private readonly IFfxedProcessStarter processStarter;
    private readonly string expectedJarSha256;

    public FfxedRuntimeLauncher()
        : this(
            AppContext.BaseDirectory,
            new BundledJavaRuntimeLocator(),
            new SystemFfxedProcessStarter(),
            ExpectedJarSha256)
    {
    }

    public FfxedRuntimeLauncher(
        string baseDirectory,
        BundledJavaRuntimeLocator runtimeLocator,
        IFfxedProcessStarter processStarter,
        string expectedJarSha256 = ExpectedJarSha256)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Path.IsPathRooted(baseDirectory))
        {
            throw new ArgumentException("FFXED application base must be absolute.", nameof(baseDirectory));
        }
        if (!IsSha256(expectedJarSha256))
        {
            throw new ArgumentException("FFXED expected JAR hash must be SHA-256.", nameof(expectedJarSha256));
        }

        this.baseDirectory = Path.GetFullPath(baseDirectory);
        this.runtimeLocator = runtimeLocator ?? throw new ArgumentNullException(nameof(runtimeLocator));
        this.processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        this.expectedJarSha256 = expectedJarSha256;
    }

    public FfxedLaunchPreparation Prepare()
    {
        JavaRuntimeLocation? runtime = runtimeLocator.Locate();
        if (runtime is not { Source: JavaRuntimeSource.BundledPrivate, IsPrivateRuntime: true })
        {
            return Failure(FfxedLaunchFailure.PrivateJavaRuntimeMissing);
        }

        string jarPath = Path.Combine(baseDirectory, "ExternalLibs", "FFXED", "FFXED.jar");
        if (!File.Exists(jarPath))
        {
            return Failure(FfxedLaunchFailure.BundledJarMissing);
        }

        string jarSha256;
        string runtimeSha256;
        try
        {
            jarSha256 = HashFile(jarPath);
            runtimeSha256 = HashFile(runtime.ExecutablePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(FfxedLaunchFailure.ProcessStartFailed, ex.GetType().Name);
        }

        if (!string.Equals(jarSha256, expectedJarSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new FfxedLaunchPreparation(
                FfxedLaunchFailure.JarHashMismatch,
                null,
                jarSha256,
                runtimeSha256,
                null);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = runtime.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(jarPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jarPath);
        startInfo.Environment.Remove("JAVA_HOME");

        return new FfxedLaunchPreparation(
            FfxedLaunchFailure.None,
            startInfo,
            jarSha256,
            runtimeSha256,
            null);
    }

    public FfxedLaunchResult Launch()
    {
        FfxedLaunchPreparation preparation = Prepare();
        if (preparation.Failure != FfxedLaunchFailure.None || preparation.StartInfo == null)
        {
            DebugLog.Warn("ReleaseRuntime.FFXED", $"Launch blocked: {preparation.Failure}.");
            return new FfxedLaunchResult(
                false,
                preparation.Failure,
                preparation.JarSha256,
                preparation.RuntimeSha256,
                preparation.Detail);
        }

        try
        {
            bool started = processStarter.Start(preparation.StartInfo);
            if (!started)
            {
                return new FfxedLaunchResult(
                    false,
                    FfxedLaunchFailure.ProcessStartFailed,
                    preparation.JarSha256,
                    preparation.RuntimeSha256,
                    null);
            }

            DebugLog.Info(
                "ReleaseRuntime.FFXED",
                $"Started bundled FFXED; jar={preparation.JarSha256![..12]} runtime={preparation.RuntimeSha256![..12]}.");
            return new FfxedLaunchResult(
                true,
                FfxedLaunchFailure.None,
                preparation.JarSha256,
                preparation.RuntimeSha256,
                null);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or Win32Exception)
        {
            DebugLog.Error("ReleaseRuntime.FFXED", $"Launch failed ({ex.GetType().Name}).");
            return new FfxedLaunchResult(
                false,
                FfxedLaunchFailure.ProcessStartFailed,
                preparation.JarSha256,
                preparation.RuntimeSha256,
                ex.Message);
        }
    }

    private static FfxedLaunchPreparation Failure(FfxedLaunchFailure failure, string? detail = null) =>
        new(failure, null, null, null, detail);

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);
}
