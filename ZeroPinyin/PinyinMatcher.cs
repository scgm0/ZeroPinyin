using System.Runtime.CompilerServices;

namespace ZeroPinyin;

public sealed class PinyinMatcher {
	private readonly HanziPinyinMap _map;
	private readonly PinyinPrefixData _prefixData;
	private readonly FuzzyConfig _config;

	private string? _lastSearchString;
	private PinyinQuery? _lastSearchQuery;

	private readonly Dictionary<string, PinyinQuery> _cache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PinyinQuery>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;

	private readonly string?[] _fifoKeys = new string?[1024];
	private int _fifoIndex;
	private readonly Lock _lock = new();

	public static PinyinMatcher Default { get; } = new(HanziPinyinMap.Default);

	public PinyinMatcher(HanziPinyinMap map, FuzzyConfig? fuzzy = null) {
		_map = map;
		_config = fuzzy ?? FuzzyConfig.Default;
		_prefixData = PrefixMapBuilder.Build(map, _config);
		_cacheLookup = _cache.GetAlternateLookup<ReadOnlySpan<char>>();
	}

	public PinyinQuery Compile(ReadOnlySpan<char> search) => GetOrCompileSpan(search);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || !text.IsEmpty && GetOrCompileSpan(search).Contains(text);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CountMatches(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || text.IsEmpty ? 0 : GetOrCompileSpan(search).CountMatches(text);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool StartsWith(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || !text.IsEmpty && GetOrCompileSpan(search).StartsWith(text);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool EndsWith(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || !text.IsEmpty && GetOrCompileSpan(search).EndsWith(text);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty ? text.IsEmpty : !text.IsEmpty && GetOrCompileSpan(search).IsMatch(text);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private PinyinQuery GetOrCompileSpan(ReadOnlySpan<char> search) {
		var last = _lastSearchString;
		if (last is not null && search.SequenceEqual(last) && _lastSearchQuery is not null) {
			return _lastSearchQuery;
		}

		lock (_lock) {
			if (_cacheLookup.TryGetValue(search, out var q)) {
				_lastSearchString = q.SearchText;
				_lastSearchQuery = q;
				return q;
			}

			q = new(search, _map, _prefixData, _config);

			var oldest = _fifoKeys[_fifoIndex];
			if (oldest is not null) {
				_cache.Remove(oldest);
			}

			_cacheLookup[search] = q;
			_fifoKeys[_fifoIndex] = q.SearchText;
			_fifoIndex = _fifoIndex + 1 & 1023;

			_lastSearchString = q.SearchText;
			_lastSearchQuery = q;
			return q;
		}
	}
}