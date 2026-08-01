namespace ZeroPinyin;

/// <summary>拼音解析器：提供声母/韵母表与音节解析、声调符号规范化。</summary>
public static class PinyinParser {
	/// <summary>声母表（按解析优先级排序，多字符声母在前）。</summary>
	public static readonly string[] InitList = [
		"zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l",
		"g", "k", "h", "j", "q", "x", "r", "z", "c", "s", "y", "w"
	];

	/// <summary>韵母表（v 表示 ü 韵母，如 lv = lü）。</summary>
	public static readonly string[] FinalList = [
		"a", "o", "e", "ai", "ei", "ao", "ou", "an", "en", "ang", "eng", "ong", "er",
		"i", "ia", "ie", "iao", "iu", "ian", "in", "iang", "ing", "iong",
		"u", "ua", "uo", "uai", "ui", "uan", "un", "uang", "ueng",
		"v", "ve", "van", "vn"
	];

	/// <summary>
	/// 将带声调符号或 ü 的拼音字符串规范化为纯 ASCII 形式（声调转为尾部数字，ü 转为 v）。
	/// 例如："yáng" → "yang2"，"lüè" → "lve4"。
	/// 注意：本方法为单音节语义（数据解析按音节逐段调用）；多音节串仅保留最后一个音调数字，
	/// 搜索端多音节精确音调请使用数字音调形式（如 "yang2mao2"）。
	/// </summary>
	/// <param name="input">输入拼音字符串。</param>
	/// <param name="buf">输出缓冲区，必须足够容纳规范化结果（最多 input 长度 + 1）。</param>
	/// <returns>规范化结果在 <paramref name="buf"/> 中的长度。</returns>
	public static int RemoveToneMarks(ReadOnlySpan<char> input, Span<char> buf) {
		int pos = 0, tone = 0;
		foreach (var c in input) {
			var (b, t) = c switch {
				'ā' => ('a', 1),
				'á' => ('a', 2),
				'ǎ' => ('a', 3),
				'à' => ('a', 4),
				'ē' => ('e', 1),
				'é' => ('e', 2),
				'ě' => ('e', 3),
				'è' => ('e', 4),
				'ī' => ('i', 1),
				'í' => ('i', 2),
				'ǐ' => ('i', 3),
				'ì' => ('i', 4),
				'ō' => ('o', 1),
				'ó' => ('o', 2),
				'ǒ' => ('o', 3),
				'ò' => ('o', 4),
				'ū' => ('u', 1),
				'ú' => ('u', 2),
				'ǔ' => ('u', 3),
				'ù' => ('u', 4),
				'ǖ' => ('v', 1),
				'ǘ' => ('v', 2),
				'ǚ' => ('v', 3),
				'ǜ' => ('v', 4),
				'ü' => ('v', 0),
				_ => (c, 0)
			};
			buf[pos++] = b;
			if (t != 0) {
				tone = t;
			}
		}

		if (tone > 0) {
			buf[pos++] = (char)('0' + tone);
		}

		return pos;
	}

	/// <summary>
	/// 解析一个拼音字符串（如 "zhong1"、"lüè"）为音节；解析失败返回 <see cref="PinyinSyllable.IsValid"/> 为 false 的默认值。
	/// </summary>
	/// <param name="input">拼音字符串（声母 + 韵母，可带尾部数字声调）。</param>
	/// <returns>解析结果。</returns>
	public static PinyinSyllable Parse(ReadOnlySpan<char> input) {
		if (input.IsEmpty) {
			return default;
		}

		byte tone = 0;
		var toneLen = 0;

		if (input.Length > 1 && char.IsAsciiDigit(input[^1]) && input[^1] <= '5') {
			tone = (byte)(input[^1] - '0');
			toneLen = 1;
		}

		var syl = input[..^toneLen];

		for (var i = 0; i < InitList.Length; i++) {
			var ini = InitList[i];
			if (syl.StartsWith(ini, StringComparison.OrdinalIgnoreCase)) {
				var finalSpan = syl[ini.Length..];
				for (var j = 0; j < FinalList.Length; j++) {
					if (finalSpan.SequenceEqual(FinalList[j])) {
						return new((byte)(i + 1), (byte)(j + 1), tone);
					}
				}

				break;
			}
		}

		for (var j = 0; j < FinalList.Length; j++) {
			if (syl.SequenceEqual(FinalList[j])) {
				return new(0, (byte)(j + 1), tone);
			}
		}

		return default;
	}
}