namespace ZeroHourStudio.Infrastructure.Caching;

/// <summary>
/// مدير التخزين المؤقت لتحسين الأداء
/// يقوم بتخزين مؤقت للملفات المستخرجة وملفات INI المحللة
/// </summary>
public class CacheManager
{
    private readonly Dictionary<string, CacheEntry<byte[]>> _fileCache;
    private readonly Dictionary<string, CacheEntry<string>> _stringCache;
    private readonly TimeSpan _defaultExpiration;

    public CacheManager(TimeSpan? defaultExpiration = null)
    {
        _fileCache = new Dictionary<string, CacheEntry<byte[]>>(StringComparer.OrdinalIgnoreCase);
        _stringCache = new Dictionary<string, CacheEntry<string>>(StringComparer.OrdinalIgnoreCase);
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromHours(1);
    }

    /// <summary>
    /// تخزين بيانات الملف في الذاكرة المؤقتة
    /// </summary>
    public void CacheFile(string key, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(key) || data == null)
            return;

        _fileCache[key] = new CacheEntry<byte[]>(data, DateTime.UtcNow.Add(_defaultExpiration));
    }

    /// <summary>
    /// استرجاع بيانات الملف من الذاكرة المؤقتة
    /// </summary>
    public byte[]? GetCachedFile(string key)
    {
        if (!_fileCache.TryGetValue(key, out var entry))
            return null;

        if (entry.IsExpired)
        {
            _fileCache.Remove(key);
            return null;
        }

        return entry.Value;
    }

    /// <summary>
    /// تخزين نص في الذاكرة المؤقتة
    /// </summary>
    public void CacheString(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(value))
            return;

        _stringCache[key] = new CacheEntry<string>(value, DateTime.UtcNow.Add(_defaultExpiration));
    }

    /// <summary>
    /// استرجاع نص من الذاكرة المؤقتة
    /// </summary>
    public string? GetCachedString(string key)
    {
        if (!_stringCache.TryGetValue(key, out var entry))
            return null;

        if (entry.IsExpired)
        {
            _stringCache.Remove(key);
            return null;
        }

        return entry.Value;
    }

    /// <summary>
    /// التحقق من وجود عنصر في الذاكرة المؤقتة
    /// </summary>
    public bool HasCachedFile(string key) => _fileCache.ContainsKey(key) && !_fileCache[key].IsExpired;

    /// <summary>
    /// التحقق من وجود نص في الذاكرة المؤقتة
    /// </summary>
    public bool HasCachedString(string key) => _stringCache.ContainsKey(key) && !_stringCache[key].IsExpired;

    /// <summary>
    /// مسح الذاكرة المؤقتة بالكامل
    /// </summary>
    public void Clear()
    {
        _fileCache.Clear();
        _stringCache.Clear();
    }

    /// <summary>
    /// مسح العناصر المنتهية الصلاحية
    /// </summary>
    public void RemoveExpiredEntries()
    {
        var expiredFiles = _fileCache
            .Where(x => x.Value.IsExpired)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expiredFiles)
            _fileCache.Remove(key);

        var expiredStrings = _stringCache
            .Where(x => x.Value.IsExpired)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expiredStrings)
            _stringCache.Remove(key);
    }

    /// <summary>
    /// الحصول على حجم الذاكرة المؤقتة بالبايت
    /// </summary>
    public long GetCacheSize()
    {
        long size = 0;

        foreach (var entry in _fileCache.Values)
        {
            size += entry.Value?.Length ?? 0;
        }

        foreach (var entry in _stringCache.Values)
        {
            size += System.Text.Encoding.UTF8.GetByteCount(entry.Value ?? "");
        }

        return size;
    }
}

/// <summary>
/// يمثل عنصر مخزن مؤقتاً مع معلومات الصلاحية
/// </summary>
internal class CacheEntry<T>
{
    public T Value { get; }
    public DateTime ExpiresAt { get; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    public CacheEntry(T value, DateTime expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
    }
}
