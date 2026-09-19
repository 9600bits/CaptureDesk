namespace CaptureDesk.Core;

public sealed class MemoryHistoryStore(int limit = 50) : IHistoryStore
{
    private readonly LinkedList<CaptureResult> _items = new();
    private int _limit = Math.Clamp(limit, 1, 500);
    public void SetLimit(int value)
    {
        _limit = Math.Clamp(value, 1, 500);
        while (_items.Count > _limit) _items.RemoveLast();
    }
    public void Add(CaptureResult result)
    {
        _items.AddFirst(result);
        while (_items.Count > _limit) _items.RemoveLast();
    }
    public IReadOnlyList<CaptureResult> GetRecent() => _items.ToList();
}
