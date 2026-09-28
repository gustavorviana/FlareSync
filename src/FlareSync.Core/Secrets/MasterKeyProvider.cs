using System.Security.Cryptography;
using FlareSync.Core.Config;

namespace FlareSync.Core.Secrets;

/// <summary>
/// Supplies the 32-byte master key used to encrypt secrets. Sources, in order:
/// systemd credential <c>flaresync-key</c>, <c>FLARESYNC_MASTER_KEY</c> (base64), <c>config/secret.key</c>.
/// </summary>
public sealed class MasterKeyProvider(ConfigPaths paths)
{
    public const int KeySize = 32;
    public const string EnvironmentVariable = "FLARESYNC_MASTER_KEY";
    public const string SystemdCredentialName = "flaresync-key";

    private readonly Lock _lock = new();
    private byte[]? _cached;

    /// <summary>Human-readable description of where the key comes from, or <c>null</c> when none is available.</summary>
    public string? DescribeSource()
    {
        if (SystemdCredentialPath() is { } credential && File.Exists(credential))
        {
            return $"systemd credential '{SystemdCredentialName}'";
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            return $"environment variable {EnvironmentVariable}";
        }

        return File.Exists(paths.SecretKeyFile) ? $"key file '{paths.SecretKeyFile}'" : null;
    }

    /// <summary>Returns the key, throwing when no source is available.</summary>
    public byte[] GetKey()
    {
        lock (_lock)
        {
            return _cached ??= TryLoad() ?? throw new FlareSyncException(
                $"No master key found. Run 'flaresync init' or provide the key through {EnvironmentVariable} " +
                $"or the systemd credential '{SystemdCredentialName}'.");
        }
    }

    /// <summary>Returns the key, creating <c>secret.key</c> when no source is available.</summary>
    public byte[] GetOrCreateKey()
    {
        lock (_lock)
        {
            if ((_cached ??= TryLoad()) is { } key)
            {
                return key;
            }

            EnsureKeyFile();
            return _cached = TryLoad()!;
        }
    }

    /// <summary>Creates <c>secret.key</c> when no key source exists. Returns <c>true</c> when a file was created.</summary>
    public bool EnsureKeyFile()
    {
        if (DescribeSource() is not null)
        {
            return false;
        }

        JsonConfigStore.EnsureDirectory(paths.Root);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(paths.SecretKeyFile, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize)));
        }

        return true;
    }

    private byte[]? TryLoad()
    {
        if (SystemdCredentialPath() is { } credential && File.Exists(credential))
        {
            return Decode(File.ReadAllText(credential), $"systemd credential '{SystemdCredentialName}'");
        }

        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } fromEnvironment)
        {
            return Decode(fromEnvironment, EnvironmentVariable);
        }

        return File.Exists(paths.SecretKeyFile)
            ? Decode(File.ReadAllText(paths.SecretKeyFile), paths.SecretKeyFile)
            : null;
    }

    private static string? SystemdCredentialPath()
        => Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY") is { Length: > 0 } directory
            ? Path.Combine(directory, SystemdCredentialName)
            : null;

    private static byte[] Decode(string value, string source)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException ex)
        {
            throw new FlareSyncException($"Master key from {source} is not valid base64.", ex);
        }

        if (key.Length != KeySize)
        {
            throw new FlareSyncException($"Master key from {source} must be {KeySize} bytes, got {key.Length}.");
        }

        return key;
    }
}
