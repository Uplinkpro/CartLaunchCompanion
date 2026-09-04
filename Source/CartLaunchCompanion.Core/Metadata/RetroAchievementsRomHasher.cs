using System.Security.Cryptography;

namespace CartLaunchCompanion.Core.Metadata;

public sealed record RetroAchievementsHashResult(
    bool Supported,
    string Hash,
    string Method,
    string Message);

public sealed class RetroAchievementsRomHasher
{
    private const int BufferSize = 1024 * 128;

    public async Task<RetroAchievementsHashResult> HashAsync(
        string filePath,
        string platformLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            return Unsupported("The selected ROM file no longer exists.");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var platform = Normalize(platformLabel);

        if (extension is ".chd" or ".cso" or ".rvz" or ".cue" or ".iso" or ".pbp")
            return Unsupported("This disc or compressed-container format requires the emulator's rcheevos hash. Enter the RetroAchievements game ID manually, or copy the RA hash shown by the emulator.");

        if (extension is ".zip" or ".7z")
        {
            if (platform.Contains("arcade", StringComparison.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                return Success(Md5(System.Text.Encoding.UTF8.GetBytes(name)), "Arcade set name");
            }
            return Unsupported("Archives cannot be identified safely without extracting the exact game file first.");
        }

        if (extension == ".nes" || platform.Contains("nintendoentertainmentsystem", StringComparison.Ordinal))
            return await HashWithOptionalHeaderAsync(filePath, 16, [0x4E, 0x45, 0x53, 0x1A], "NES header removed", cancellationToken);

        if (extension == ".fds")
            return await HashWithOptionalHeaderAsync(filePath, 16, [0x46, 0x44, 0x53, 0x1A], "FDS header removed", cancellationToken);

        if (extension == ".lnx")
            return await HashWithOptionalHeaderAsync(filePath, 64, [0x4C, 0x59, 0x4E, 0x58, 0x00], "Lynx header removed", cancellationToken);

        if (extension == ".a78")
            return await HashWithOptionalHeaderAsync(filePath, 128, [0x01, 0x41, 0x54, 0x41, 0x52, 0x49, 0x37, 0x38, 0x30, 0x30], "Atari 7800 header removed", cancellationToken);

        if (extension is ".sfc" or ".smc" || platform.Contains("supernintendo", StringComparison.Ordinal) || platform.Contains("superfamicom", StringComparison.Ordinal))
        {
            var length = new FileInfo(filePath).Length;
            var offset = length % 8192 == 512 ? 512 : 0;
            return Success(await Md5FileAsync(filePath, offset, null, cancellationToken), offset == 0 ? "Full-file MD5" : "SNES copier header removed");
        }

        if (extension is ".n64" or ".v64" or ".z64")
            return await HashNintendo64Async(filePath, extension, cancellationToken);

        if (extension == ".nds")
            return await HashNintendoDsAsync(filePath, cancellationToken);

        if (WholeFileExtensions.Contains(extension))
            return Success(await Md5FileAsync(filePath, 0, null, cancellationToken), "Full-file MD5");

        return Unsupported($"CLC does not yet have an authoritative RetroAchievements hash recipe for {extension} files.");
    }

    private static readonly HashSet<string> WholeFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gb", ".gbc", ".gba", ".gen", ".md", ".smd", ".gg", ".sms", ".32x",
        ".a26", ".j64", ".min", ".vb", ".ngp", ".ngc", ".ws", ".wsc", ".wasm"
    };

    private static async Task<RetroAchievementsHashResult> HashWithOptionalHeaderAsync(
        string path, int headerSize, byte[] signature, string headerMethod, CancellationToken cancellationToken)
    {
        var header = new byte[signature.Length];
        await using (var stream = File.OpenRead(path))
            _ = await stream.ReadAsync(header, cancellationToken);
        var hasHeader = header.AsSpan().SequenceEqual(signature);
        return Success(
            await Md5FileAsync(path, hasHeader ? headerSize : 0, null, cancellationToken),
            hasHeader ? headerMethod : "Full-file MD5");
    }

    private static async Task<RetroAchievementsHashResult> HashNintendo64Async(
        string path, string extension, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (extension == ".v64")
            for (var index = 0; index + 1 < bytes.Length; index += 2)
                (bytes[index], bytes[index + 1]) = (bytes[index + 1], bytes[index]);
        else if (extension == ".n64")
            for (var index = 0; index + 3 < bytes.Length; index += 4)
                (bytes[index], bytes[index + 3], bytes[index + 1], bytes[index + 2]) =
                    (bytes[index + 3], bytes[index], bytes[index + 2], bytes[index + 1]);
        return Success(Md5(bytes), extension == ".z64" ? "N64 big-endian MD5" : "N64 normalized big-endian MD5");
    }

    private static async Task<RetroAchievementsHashResult> HashNintendoDsAsync(
        string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length < 0x160)
            return Unsupported("The Nintendo DS file is too small to contain a valid header.");

        var iconOffset = ReadUInt32(bytes, 0x68);
        var arm9Offset = ReadUInt32(bytes, 0x20);
        var arm9Size = ReadUInt32(bytes, 0x2C);
        var arm7Offset = ReadUInt32(bytes, 0x30);
        var arm7Size = ReadUInt32(bytes, 0x3C);
        if (!Fits(bytes, iconOffset, 0xA00) || !Fits(bytes, arm9Offset, arm9Size) || !Fits(bytes, arm7Offset, arm7Size))
            return Unsupported("The Nintendo DS header points outside the selected file.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(bytes.AsSpan(0, 0x160));
        hash.AppendData(bytes.AsSpan((int)arm9Offset, (int)arm9Size));
        hash.AppendData(bytes.AsSpan((int)arm7Offset, (int)arm7Size));
        hash.AppendData(bytes.AsSpan((int)iconOffset, 0xA00));
        return Success(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), "Nintendo DS executable and icon data");
    }

    private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
    private static bool Fits(byte[] bytes, uint offset, uint length) => offset <= bytes.Length && length <= bytes.Length - offset;

    private static async Task<string> Md5FileAsync(
        string path, long offset, long? length, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
        stream.Position = offset;
        var remaining = length ?? (stream.Length - offset);
        var buffer = new byte[BufferSize];
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Md5(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static RetroAchievementsHashResult Success(string hash, string method) => new(true, hash, method, "ROM hash calculated.");
    private static RetroAchievementsHashResult Unsupported(string message) => new(false, "", "Unsupported", message);
}
