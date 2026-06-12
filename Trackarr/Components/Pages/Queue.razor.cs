using Microsoft.AspNetCore.Components;

namespace Trackarr.Components.Pages;

public partial class Queue : ComponentBase {
    private const int MaxStrikes = 3;

    private List<ActionDownloadRow> _actionDownloads = [
        new() {
            Type = "M",
            Title = "The Matrix",
            Server = "Radarr",
            Status = "Warning",
            Strikes = 1
        },
        new() {
            Type = "S",
            Title = "Breaking Bad",
            Server = "Sonarr",
            Status = "Warning",
            Strikes = 2
        },
        new() {
            Type = "M",
            Title = "Interstellar",
            Server = "Radarr",
            Status = "Failed",
            Strikes = 3
        },
        new() {
            Type = "S",
            Title = "The Bear",
            Server = "Sonarr",
            Status = "Warning",
            Strikes = 1
        },
        new() {
            Type = "M",
            Title = "Dune",
            Server = "Radarr",
            Status = "Failed",
            Strikes = 3
        },
        new() {
            Type = "S",
            Title = "Severance",
            Server = "Sonarr",
            Status = "Warning",
            Strikes = 2
        }
    ];

    private int _actionPage = 1;
    private int _actionPageSize = 5;

    private List<ActiveDownloadRow> _activeDownloads = [
        new() {
            Type = "M",
            Title = "Blade Runner 2049",
            Server = "Radarr",
            Indexer = "The Pirate Bay",
            Progress = 64,
            Size = "18.2 GB",
            Eta = "24m"
        },
        new() {
            Type = "S",
            Title = "Silo S02E04",
            Server = "Sonarr",
            Indexer = "NZBGeek",
            Progress = 31,
            Size = "3.4 GB",
            Eta = "11m"
        }
    ];

    private List<MissingMediaRow> _missingMedia = [
        new() {
            Type = "M",
            Title = "Arrival",
            Server = "Radarr",
            QualityTarget = "1080p",
            LastSearch = "2h ago",
            Attempts = 1
        },
        new() {
            Type = "S",
            Title = "The Last of Us S01E03",
            Server = "Sonarr",
            QualityTarget = "WEB-1080p",
            LastSearch = "4h ago",
            Attempts = 2
        }
    ];

    private string _selectedActionServer = string.Empty;

    private List<UnavailableMediaRow> _unavailableMedia = [
        new() {
            Type = "M",
            Title = "Heat",
            Server = "Radarr",
            Reason = "No matching releases found",
            OriginalQuality = "2160p",
            CurrentQuality = "1080p",
            Attempts = 4,
            LastSearch = "1d ago"
        },
        new() {
            Type = "S",
            Title = "Dark S03E07",
            Server = "Sonarr",
            Reason = "Rejected by quality profile",
            OriginalQuality = "WEB-1080p",
            CurrentQuality = "HDTV-720p",
            Attempts = 5,
            LastSearch = "2d ago"
        }
    ];

    private string SelectedActionServer {
        get => _selectedActionServer;
        set {
            if (_selectedActionServer == value) {
                return;
            }

            _selectedActionServer = value;
            _actionPage = 1;
        }
    }

    private List<string> ActionServers =>
        _actionDownloads.Select(x => x.Server).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    private List<ActionDownloadRow> FilteredActionDownloads {
        get {
            IEnumerable<ActionDownloadRow> query = _actionDownloads;

            if (!string.IsNullOrWhiteSpace(SelectedActionServer)) {
                query = query.Where(x => string.Equals(x.Server, SelectedActionServer, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }
    }

    private List<ActionDownloadRow> PagedActionDownloads =>
        FilteredActionDownloads.Skip((_actionPage - 1) * _actionPageSize).Take(_actionPageSize).ToList();

    private int ActionTotalPages =>
        Math.Max(1, (int)Math.Ceiling(FilteredActionDownloads.Count / (double)_actionPageSize));

    private int ActionPageStart =>
        FilteredActionDownloads.Count == 0 ? 0 : (_actionPage - 1) * _actionPageSize + 1;

    private int ActionPageEnd =>
        Math.Min(_actionPage * _actionPageSize, FilteredActionDownloads.Count);

    private void PreviousActionPage() {
        if (_actionPage > 1) {
            _actionPage--;
        }
    }

    private void NextActionPage() {
        if (_actionPage < ActionTotalPages) {
            _actionPage++;
        }
    }

    private static string StrikePipClass(int strikes, bool isWarning, int strikeIndex) {
        if (strikeIndex >= strikes) {
            return string.Empty;
        }

        return isWarning ? "warning" : "danger";
    }

    private class ActionDownloadRow {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Strikes { get; set; }
    }

    private class ActiveDownloadRow {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Indexer { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string Size { get; set; } = string.Empty;
        public string Eta { get; set; } = string.Empty;
    }

    private class MissingMediaRow {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string QualityTarget { get; set; } = string.Empty;
        public string LastSearch { get; set; } = string.Empty;
        public int Attempts { get; set; }
    }

    private class UnavailableMediaRow {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string OriginalQuality { get; set; } = string.Empty;
        public string CurrentQuality { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string LastSearch { get; set; } = string.Empty;
    }
}