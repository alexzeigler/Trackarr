using System.Runtime.Serialization;

namespace Trackarr.Core.Queue;

public enum QueueStatus
{
    [EnumMember(Value = "ok")] Ok,
    [EnumMember(Value = "warning")] Warning,
    [EnumMember(Value = "error")] Error,
    [EnumMember(Value = "pending")] Pending,
    [EnumMember(Value = "completed")] Completed
}