namespace Trackarr.Core.Health;

public class HealthDecision {
    public int DownloadAttemptId { get; set; }

    public bool ShouldRemove { get; set; }
    public bool ShouldReplace { get; set; }
    public bool ShouldProtect { get; set; }

    public HealthState State { get; set; }

    public decimal RawScore { get; set; }
    public int VisibleScore { get; set; }
    public int Confidence { get; set; }
    public int RecoveryCredit { get; set; }

    public string ReasonKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public List<string> Restrictions { get; set; } = [];
}