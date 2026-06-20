using System.Runtime.Serialization;

namespace Trackarr.Core.Queue;

public enum QueueState
{
    [EnumMember(Value = "downloading")] Downloading,

    [EnumMember(Value = "importing")] Importing,

    [EnumMember(Value = "pendingImport")] PendingImport,

    [EnumMember(Value = "pendingFailure")] PendingFailure,

    [EnumMember(Value = "failed")] Failed,

    [EnumMember(Value = "imported")] Imported,

    [EnumMember(Value = "importBlocked")] ImportBlocked,

    [EnumMember(Value = "stalled")] Stalled,

    [EnumMember(Value = "downloadingMetadata")]
    DownloadingMetadata,

    [EnumMember(Value = "warning")] Warning,
    Unknown
}