namespace Trackarr.Components.Tables;

public interface IAppTableState
{
    void SortBy(string key);
    bool IsSorted(string key);
    bool IsDescending(string key);
    string SortClass(string key);
}