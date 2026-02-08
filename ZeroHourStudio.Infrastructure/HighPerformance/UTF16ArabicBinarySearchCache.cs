using System.Collections.Concurrent;
using System.Text;

namespace ZeroHourStudio.Infrastructure.HighPerformance;

/// <summary>
/// „Õ—ﬂ «·»ÕÀ «·À‰«∆Ì «·„ ﬁœ„ (UTF-16 Arabic Binary Search Engine)
/// - Ìœ⁄„ ”·«”· UTF-16 «·⁄—»Ì… »ﬂ›«¡… ⁄«·Ì…
/// - Ì” Œœ„ Caching –ﬂÌ ·„‰⁄  ﬂ—«— «·„⁄«·Ã…
/// - Ìœ⁄„ «·»ÕÀ «·”—Ì⁄ ›Ì «·ÂÌ«ﬂ· «·»Ì«‰Ì… «·÷Œ„…
/// </summary>
public class UTF16ArabicBinarySearchCache : IDisposable
{
    private readonly ConcurrentDictionary<string, int[]> _searchIndexCache;
    private readonly ConcurrentDictionary<string, string[]> _tokenCache;
    private readonly ConcurrentDictionary<string, Dictionary<string, int>> _frequencyCache;
    private bool _disposed;

    public UTF16ArabicBinarySearchCache(int initialCapacity = 10000)
    {
        _searchIndexCache = new ConcurrentDictionary<string, int[]>(StringComparer.Ordinal);
        _tokenCache = new ConcurrentDictionary<string, string[]>(StringComparer.Ordinal);
        _frequencyCache = new ConcurrentDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
    }

    /// <summary>
    /// »‰«¡ ›Â—” »ÕÀ À‰«∆Ì „‰ ‰’ ﬂ»Ì—
    /// </summary>
    public int[] BuildSearchIndex(string text, string delimiter = " ")
    {
        ThrowIfDisposed();

        string cacheKey = $"index_{text.GetHashCode()}_{delimiter}";
        if (_searchIndexCache.TryGetValue(cacheKey, out var cachedIndex))
        {
            return cachedIndex;
        }

        var tokens = text.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
        var index = new int[tokens.Length + 1];

        index[0] = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            index[i + 1] = index[i] + tokens[i].Length + delimiter.Length;
        }

        _searchIndexCache.TryAdd(cacheKey, index);
        return index;
    }

    /// <summary>
    /// «·»ÕÀ «·À‰«∆Ì ⁄‰ ﬂ·„… „⁄Ì‰… „⁄ œ⁄„ «·⁄—»Ì… Ê«·Õ«·«  «·Õ”«”…
    /// </summary>
    public int BinarySearchArabic(string[] items, string target, StringComparison comparison = StringComparison.Ordinal)
    {
        ThrowIfDisposed();

        int left = 0;
        int right = items.Length - 1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            int cmp = string.Compare(items[mid], target, comparison);

            if (cmp == 0)
                return mid;
            else if (cmp < 0)
                left = mid + 1;
            else
                right = mid - 1;
        }

        return -1;
    }

    /// <summary>
    /// «·»ÕÀ ⁄‰ ‰ÿ«ﬁ „‰ «·ﬂ·„«  «·„ ‘«»Â… („À· Ã„Ì⁄ «·√”·Õ… «· Ì  »œ√ »‹ "M")
    /// </summary>
    public (int Start, int End) BinarySearchRangeArabic(
        string[] items,
        string prefix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ThrowIfDisposed();

        // «·»ÕÀ ⁄‰ »œ«Ì… «·‰ÿ«ﬁ
        int left = 0;
        int right = items.Length - 1;
        int startIndex = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            if (items[mid].StartsWith(prefix, comparison))
            {
                startIndex = mid;
                right = mid - 1;
            }
            else if (string.Compare(items[mid], prefix, comparison) < 0)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        if (startIndex == -1)
            return (-1, -1);

        // «·»ÕÀ ⁄‰ ‰Â«Ì… «·‰ÿ«ﬁ
        left = startIndex;
        right = items.Length - 1;
        int endIndex = startIndex;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            if (items[mid].StartsWith(prefix, comparison))
            {
                endIndex = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return (startIndex, endIndex);
    }

    /// <summary>
    /// „⁄«·Ã… «·‰’Ê’ «·⁄—»Ì… »ﬂ›«¡… (Normalization + Tokenization)
    /// </summary>
    public string[] TokenizeArabicText(string text, bool normalize = true)
    {
        ThrowIfDisposed();

        string cacheKey = $"tokens_{text.GetHashCode()}_{normalize}";
        if (_tokenCache.TryGetValue(cacheKey, out var cachedTokens))
        {
            return cachedTokens;
        }

        string processed = text;
        if (normalize)
        {
            //  ÿ»Ì⁄ «·‰’ «·⁄—»Ì (≈“«·… «·Õ—Ê› «·≈÷«›Ì…° «·‹ diacritics° ≈·Œ)
            processed = NormalizeArabicText(text);
        }

        // ›’· «·ﬂ·„«  »‰«¡ ⁄·Ï «·„”«›«  Ê«·⁄·«„« 
        var tokens = processed.Split(new[] { ' ', '\t', '\n', '\r', '_', '-', '/', '\\' }, 
            StringSplitOptions.RemoveEmptyEntries);

        _tokenCache.TryAdd(cacheKey, tokens);
        return tokens;
    }

    /// <summary>
    ///  ÿ»Ì⁄ «·‰’ «·⁄—»Ì
    /// </summary>
    public string NormalizeArabicText(string text)
    {
        // ≈“«·… «·Õ—Ê› «·≈÷«›Ì… Ê«·⁄·«„«  ›Ì «·⁄—»Ì…
        var normalized = new StringBuilder();

        foreach (char c in text)
        {
            // «·Õ›«Ÿ ⁄·Ï «·Õ—Ê› «·√”«”Ì… ›ﬁÿ
            if (char.IsLetterOrDigit(c) || c == ' ')
            {
                normalized.Append(c);
            }
        }

        return normalized.ToString();
    }

    /// <summary>
    /// Õ”«»  ﬂ—«— «·ﬂ·„«  (Word Frequency Analysis)
    /// „›Ìœ ·«ﬂ ‘«› «·√‰„«ÿ Ê«· Õ”Ì‰« 
    /// </summary>
    public Dictionary<string, int> AnalyzeWordFrequency(string text, string delimiter = " ")
    {
        ThrowIfDisposed();

        string cacheKey = $"freq_{text.GetHashCode()}_{delimiter}";
        if (_frequencyCache.TryGetValue(cacheKey, out var cachedFreq))
        {
            return new Dictionary<string, int>(cachedFreq);
        }

        var tokens = text.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            string normalized = NormalizeArabicText(token);
            if (!string.IsNullOrEmpty(normalized))
            {
                if (frequency.ContainsKey(normalized))
                    frequency[normalized]++;
                else
                    frequency[normalized] = 1;
            }
        }

        _frequencyCache.TryAdd(cacheKey, frequency);
        return frequency;
    }

    /// <summary>
    /// «·»ÕÀ «·›«“Ì (Fuzzy Search) „⁄ œ⁄„ «·⁄—»Ì…
    /// ÌÃœ «·ﬂ·„«  «·„‘«»Â… Õ Ï ·Ê ﬂ«‰ Â‰«ﬂ √Œÿ«¡ ≈„·«∆Ì…
    /// </summary>
    public List<(string Word, int Distance)> FuzzySearchArabic(
        string[] items,
        string target,
        int maxDistance = 2)
    {
        ThrowIfDisposed();

        var results = new List<(string, int)>();

        foreach (var item in items)
        {
            int distance = LevenshteinDistanceArabic(item, target);
            if (distance <= maxDistance)
            {
                results.Add((item, distance));
            }
        }

        //  — Ì» «·‰ «∆Ã »‰«¡ ⁄·Ï «·„”«›…
        return results.OrderBy(x => x.Item2).ToList();
    }

    /// <summary>
    /// Õ”«» „”«›… Levenshtein „⁄ œ⁄„ «·√Õ—› «·⁄—»Ì…
    /// </summary>
    public int LevenshteinDistanceArabic(string source, string target)
    {
        if (source.Length == 0)
            return target.Length;
        if (target.Length == 0)
            return source.Length;

        int[,] matrix = new int[source.Length + 1, target.Length + 1];

        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(
                        matrix[i - 1, j] + 1,      // Õ–›
                        matrix[i, j - 1] + 1),     // ≈œ—«Ã
                    matrix[i - 1, j - 1] + cost);  // «” »œ«·
            }
        }

        return matrix[source.Length, target.Length];
    }

    /// <summary>
    ///  ’›Ì… «·ﬂ«‘ (Cache Cleanup)
    /// </summary>
    public void ClearCache()
    {
        ThrowIfDisposed();

        _searchIndexCache.Clear();
        _tokenCache.Clear();
        _frequencyCache.Clear();
    }

    /// <summary>
    /// «·Õ’Ê· ⁄·Ï ≈Õ’«∆Ì«  «·ﬂ«‘
    /// </summary>
    public (int SearchIndexCount, int TokenCount, int FrequencyCount) GetCacheStats()
    {
        ThrowIfDisposed();

        return (
            _searchIndexCache.Count,
            _tokenCache.Count,
            _frequencyCache.Count
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _searchIndexCache.Clear();
        _tokenCache.Clear();
        _frequencyCache.Clear();

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UTF16ArabicBinarySearchCache));
    }
}
