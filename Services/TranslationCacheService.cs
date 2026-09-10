using System.Collections.Concurrent;
using Valtrans.Models;

namespace Valtrans.Services;

/// <summary>Phase 10: repeat tactical/chat phrase cache keyed by normalized source.</summary>
public sealed class TranslationCacheService
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxEntries = 512;

    public bool TryGet(string source, string targetLanguage, string style, AppSettings settings, out string translated)
    {
        translated = "";
        var key = BuildKey(source, targetLanguage, style, settings);
        return _cache.TryGetValue(key, out translated!);
    }

    public void Set(string source, string targetLanguage, string style, AppSettings settings, string translated)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated)) return;
        if (_cache.Count >= MaxEntries)
        {
            var first = _cache.Keys.FirstOrDefault();
            if (first is not null) _cache.TryRemove(first, out _);
        }
        _cache[BuildKey(source, targetLanguage, style, settings)] = translated;
    }

    private static string BuildKey(string source, string targetLanguage, string style, AppSettings settings)
    {
        var normalized = string.Concat(source.Trim().Normalize(System.Text.NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{settings.Game}|{targetLanguage}|{style}|{normalized}";
    }
}
