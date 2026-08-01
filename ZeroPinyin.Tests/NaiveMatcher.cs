namespace ZeroPinyin.Tests;

/// <summary>
/// 朴素拼音匹配验证器：独立于 NFA 引擎的简单实现，用于对照测试。
/// 仅支持无模糊配置（FuzzyConfig 全关 + ExactMatchForHanzi=true）下的：
/// 纯汉字搜索串（精确子串）与纯拼音搜索串（音节前缀递归匹配）。
/// </summary>
static class NaiveMatcher {
	public static bool Contains(ReadOnlySpan<char> text, ReadOnlySpan<char> search) {
		if (search.IsEmpty) {
			return true;
		}

		if (text.IsEmpty) {
			return false;
		}

		if (IsAllHanzi(search)) {
			return text.IndexOf(search, StringComparison.Ordinal) >= 0;
		}

		if (IsAllPinyin(search)) {
			for (var start = 0; start < text.Length; start++) {
				if (MatchFrom(text, start, search, 0)) {
					return true;
				}
			}

			return false;
		}

		throw new NotSupportedException("朴素验证器不支持混合搜索串");
	}

	static private bool IsAllHanzi(ReadOnlySpan<char> s) {
		foreach (var c in s) {
			if (char.IsAscii(c)) {
				return false;
			}
		}

		return true;
	}

	static private bool IsAllPinyin(ReadOnlySpan<char> s) {
		foreach (var c in s) {
			if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '5')) {
				return false;
			}
		}

		return true;
	}

	static private bool MatchFrom(ReadOnlySpan<char> text, int i, ReadOnlySpan<char> search, int pos) {
		if (pos == search.Length) {
			return true;
		}

		if (i >= text.Length) {
			return false;
		}

		var maxK = Math.Min(7, search.Length - pos);
		for (var k = 1; k <= maxK; k++) {
			if (CharMatchesPrefix(text[i], search.Slice(pos, k)) && MatchFrom(text, i + 1, search, pos + k)) {
				return true;
			}
		}

		return false;
	}

	static private bool CharMatchesPrefix(char c, ReadOnlySpan<char> prefix) {
		var map = HanziPinyinMap.Default;
		var cls = map.CharToClass[c];
		if (cls == 0) {
			return false;
		}

		var range = map.ClassRanges[cls];
		var hasToneDigit = prefix[^1] is >= '1' and <= '5';
		for (var idx = range.Offset; idx < range.Offset + range.Count; idx++) {
			var syl = map.FlatSyllables[idx];
			if (hasToneDigit) {
				if (syl.Numbered.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
			} else if (syl.Plain.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}

		return false;
	}
}
