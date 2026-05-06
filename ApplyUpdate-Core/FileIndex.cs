using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApplyUpdate;

[JsonSerializable(typeof(FileIndex))]
public partial class FileIndexJsonContext : JsonSerializerContext;

public class FileIndex
{
    [JsonPropertyName("ver")]
    public required string Version { get; init; }

    [JsonPropertyName("forceUpdate")]
    public required bool IsForceUpdate { get; init; }

    [JsonPropertyName("time")]
    public required long UnixTimeUtc { get; init; }

    [JsonPropertyName("f")]
    public List<FileIndexEntry> FileEntries { get; init; } = [];
}

public class FileIndexEntry
{
    [JsonPropertyName("p")]
    public required string FilePath { get; init; }

    [JsonPropertyName("crc")]
    public required string Md5Hash { get; init; }

    [JsonPropertyName("s")]
    public required long FileSize { get; init; }
}