using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ZeroPinyin;

/// <summary>
/// 拼音匹配器：编译搜索串并缓存，提供多模式拼音匹配。
/// 所有匹配方法零内存分配，线程安全（缓存命中无锁）。
/// </summary>
public sealed class PinyinMatcher {
	private readonly HanziPinyinMap _map;
	private readonly PinyinPrefixData _prefixData;
	private readonly FuzzyConfig _config;

	private readonly ConcurrentDictionary<string, PinyinQuery> _cache = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, PinyinQuery>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;

	private readonly string?[] _fifoKeys = new string?[1024];
	private int _fifoIndex;
	private readonly Lock _lock = new();

	private string? _lastSearchString;
	private PinyinQuery? _lastSearchQuery;

	[ThreadStatic]
	private static (PinyinMatcher m, string s, PinyinQuery q) t_last;

	/// <summary>使用默认拼音数据与默认模糊配置的单例匹配器。</summary>
	public static PinyinMatcher Default { get; } = new(HanziPinyinMap.Default);

	/// <summary>
	/// 创建匹配器。
	/// </summary>
	/// <param name="map">汉字拼音映射表。</param>
	/// <param name="fuzzy">模糊配置，为 null 时使用 <see cref="FuzzyConfig.Default"/>。</param>
	public PinyinMatcher(HanziPinyinMap map, FuzzyConfig? fuzzy = null) {
		_map = map;
		_config = fuzzy ?? FuzzyConfig.Default;
		_prefixData = PrefixMapBuilder.Build(map, _config);
		_cacheLookup = _cache.GetAlternateLookup<ReadOnlySpan<char>>();
	}

	/// <summary>
	/// 编译搜索串为查询对象（结果缓存，相同搜索串返回同一实例）。
	/// 搜索串支持声调符号（如 "yángmáo"）与 ü（如 "lü"），自动规范化。
	/// </summary>
	/// <param name="search">拼音搜索串。</param>
	/// <returns>编译后的查询。</returns>
	public PinyinQuery Compile(ReadOnlySpan<char> search) => GetOrCompileSpan(search);

	/// <summary>判断文本中是否包含搜索串的拼音匹配。</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || !text.IsEmpty && GetOrCompileSpan(search).Contains(text);

	/// <summary>统计文本中不重叠匹配的数量。</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CountMatches(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || text.IsEmpty ? 0 : GetOrCompileSpan(search).CountMatches(text);

	/// <summary>判断文本是否以搜索串的拼音匹配开始。</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool StartsWith(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || !text.IsEmpty && GetOrCompileSpan(search).StartsWith(text);

	/// <summary>判断文本是否以搜索串的拼音匹配结束。</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool EndsWith(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty || !text.IsEmpty && GetOrCompileSpan(search).EndsWith(text);

	/// <summary>判断整个文本是否与搜索串完全匹配。</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty ? text.IsEmpty : !text.IsEmpty && GetOrCompileSpan(search).IsMatch(text);

	/// <summary>
	/// 返回文本中第一个匹配的起始索引，无匹配时返回 -1；空搜索串返回 0。
	/// </summary>
	/// <param name="text">待搜索文本。</param>
	/// <param name="search">拼音搜索串。</param>
	/// <returns>匹配起始索引，或 -1。</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int FindFirstIndex(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		search.IsEmpty ? 0 : text.IsEmpty ? -1 : GetOrCompileSpan(search).FindFirstIndex(text);

	/// <summary>
	/// 枚举文本中所有不重叠匹配的区间，零分配。
	/// </summary>
	/// <param name="text">待搜索文本。</param>
	/// <param name="search">拼音搜索串。</param>
	/// <returns>匹配区间枚举器。</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public PinyinQuery.MatchEnumerator AllMatches(ReadOnlySpan<char> text, ReadOnlySpan<char> search) =>
		GetOrCompileSpan(search).AllMatches(text);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private PinyinQuery GetOrCompileSpan(ReadOnlySpan<char> search) {
		var last = _lastSearchString;
		if (last is not null && search.SequenceEqual(last) && _lastSearchQuery is not null) {
			return _lastSearchQuery;
		}

		var t = t_last;
		if (t.m == this && t.s is not null && search.SequenceEqual(t.s)) {
			return t.q;
		}

		if (NeedsNormalization(search)) {
			Span<char> buf = stackalloc char[64];
			var normLen = PinyinParser.RemoveToneMarks(search, buf);
			return GetOrCompileSlow(buf[..normLen]);
		}

		return GetOrCompileSlow(search);
	}

	private PinyinQuery GetOrCompileSlow(ReadOnlySpan<char> search) {
		if (_cacheLookup.TryGetValue(search, out var q)) {
			SetLast(q);
			return q;
		}

		q = new(search, _map, _prefixData, _config);

		lock (_lock) {
			if (_cacheLookup.TryGetValue(search, out var existing)) {
				return SetLast(existing);
			}

			var oldest = _fifoKeys[_fifoIndex];
			if (oldest is not null) {
				_cache.TryRemove(oldest, out _);
			}

			_cache.TryAdd(q.SearchText, q);
			_fifoKeys[_fifoIndex] = q.SearchText;
			_fifoIndex = _fifoIndex + 1 & 1023;
		}

		return SetLast(q);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool NeedsNormalization(ReadOnlySpan<char> search) {
		foreach (var c in search) {
			if (c is 'ü' or 'ǖ' or 'ǘ' or 'ǚ' or 'ǜ' or
				'ā' or 'á' or 'ǎ' or 'à' or
				'ē' or 'é' or 'ě' or 'è' or
				'ī' or 'í' or 'ǐ' or 'ì' or
				'ō' or 'ó' or 'ǒ' or 'ò' or
				'ū' or 'ú' or 'ǔ' or 'ù') {
				return true;
			}
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private PinyinQuery SetLast(PinyinQuery q) {
		_lastSearchString = q.SearchText;
		_lastSearchQuery = q;
		t_last = (this, q.SearchText, q);
		return q;
	}
}