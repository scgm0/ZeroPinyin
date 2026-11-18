using System.Runtime.CompilerServices;

namespace ZeroPinyin;

/// <summary>拼音解析器：提供声母/韵母表与音节解析、声调符号规范化。</summary>
public static class PinyinParser {
	/// <summary>声母表（按解析优先级排序，多字符声母在前）。</summary>
	public static readonly string[] InitList = [
		"zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l",
		"g", "k", "h", "j", "q", "x", "r", "z", "c", "s", "y", "w"
	];

	/// <summary>
	/// 韵母表（v 表示 ü 韵母，如 lv = lü）；j/q/x/y 后的 ü 按正词法写作 u（如 xue = xüe、ju = jü）。
	/// 表尾为叹词的成音节鼻音（如 嗯 n/ng、呣 m，跟在 h 后即 hng/hm）与 ê（如 欸、誒）。
	/// </summary>
	public static readonly string[] FinalList = [
		"a", "o", "e", "ai", "ei", "ao", "ou", "an", "en", "ang", "eng", "ong", "er",
		"i", "ia", "ie", "iao", "iu", "ian", "in", "iang", "ing", "iong",
		"u", "ua", "uo", "uai", "ui", "uan", "un", "uang", "ueng", "ue",
		"v", "ve", "van", "vn",
		"m", "n", "ng", "ê"
	];

	/// <summary>判断字符是否为 j/q/x/y（ü 在正词法中写作 u 的声母，忽略大小写）。</summary>
	/// <param name="c">待判断字符。</param>
	/// <returns>是 j/q/x/y 时为 true。</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsUmlautInitial(char c) => c is 'j' or 'J' or 'q' or 'Q' or 'x' or 'X' or 'y' or 'Y';

	/// <summary>
	/// 判断字符是否需要经 <see cref="RemoveToneMarks"/> 规范化（带调元音、ü、成音节鼻音的带调形式、组合声调符）。
	/// 待归一字符均位于拉丁扩展区（U+00C0–U+1EFF），先按范围短路，纯 ASCII/汉字路径不必走比较链。
	/// 新增声调映射时必须同步本方法，否则搜索串将不会触发归一化。
	/// </summary>
	/// <param name="c">待判断字符。</param>
	/// <returns>需要规范化时为 true。</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsNormalizableChar(char c) => c is >= '\u00C0' and <= '\u1EFF' && (CombiningTone(c) != 0 || c is
		'ü' or 'ǖ' or 'ǘ' or 'ǚ' or 'ǜ' or
		'ā' or 'á' or 'ǎ' or 'à' or
		'ē' or 'é' or 'ě' or 'è' or
		'ī' or 'í' or 'ǐ' or 'ì' or
		'ō' or 'ó' or 'ǒ' or 'ò' or
		'ū' or 'ú' or 'ǔ' or 'ù' or
		'ḿ' or 'ń' or 'ň' or 'ǹ' or
		'ế' or 'ề' or 'ể' or 'ễ' or 'ệ');

	/// <summary>组合声调符对应的声调（U+0300 重音符=4、U+0301 锐音符=2、U+0304 长音符=1、U+030C 抑扬符=3）。</summary>
	/// <param name="c">待判断字符。</param>
	/// <returns>声调（1–4）；非组合声调符时为 0。</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte CombiningTone(char c) => c switch {
		'\u0304' => 1,
		'\u0301' => 2,
		'\u030C' => 3,
		'\u0300' => 4,
		_ => 0
	};

	/// <summary>
	/// 将带声调符号或 ü 的拼音字符串规范化为纯 ASCII 形式（声调转为尾部数字，ü 转为 v）。
	/// 例如："yáng" → "yang2"，"lüè" → "lve4"，"m̀" → "m4"。
	/// 组合声调符（如 U+0300、U+0304）只提取声调，不输出字符。
	/// 注意：本方法为单音节语义（数据解析按音节逐段调用）；多音节串仅保留最后一个音调数字，
	/// 搜索端多音节精确音调请使用数字音调形式（如 "yang2mao2"）。
	/// </summary>
	/// <param name="input">输入拼音字符串。</param>
	/// <param name="buf">输出缓冲区，必须足够容纳规范化结果（最多 input 长度 + 1）。</param>
	/// <returns>规范化结果在 <paramref name="buf"/> 中的长度。</returns>
	public static int RemoveToneMarks(ReadOnlySpan<char> input, Span<char> buf) {
		int pos = 0, tone = 0;
		foreach (var c in input) {
			// 组合声调符不输出字符，只提取声调（如 m̀ = m + U+0300）
			if (CombiningTone(c) is var combiningTone and not 0) {
				tone = combiningTone;
				continue;
			}

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
				'ḿ' => ('m', 2), // 成音节鼻音的带调形式（嗯/呣/噷 等叹词）
				'ń' => ('n', 2),
				'ň' => ('n', 3),
				'ǹ' => ('n', 4),
				'ế' => ('ê', 2), // ê 的带调形式（欸/誒）；更简单的 ēi/éi 等写法亦可匹配
				'ề' => ('ê', 4),
				'ể' => ('ê', 3),
				'ệ' => ('ê', 4),
				'ễ' => ('ê', 0),
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
	/// 解析一个拼音字符串（如 "zhong1"、"lüè"、"xue"）为音节；解析失败返回 <see cref="PinyinSyllable.IsValid"/> 为 false 的默认值。
	/// j/q/x/y 后的 ü 按正词法写作 u，故 "jüe"、"jve" 与 "jue" 解析为同一音节。
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

				// j/q/x/y 后的 ü 按正词法写作 u，故数据里的 xüe/xve 形式归一为 ue 与 xue 同键。
				var umlautAsU = finalSpan.Length > 0 && finalSpan[0] is 'v' or 'V' && IsUmlautInitial(ini[0]);

				for (var j = 0; j < FinalList.Length; j++) {
					var fin = FinalList[j];
					var hit = umlautAsU
						? fin.Length == finalSpan.Length && fin[0] == 'u' && finalSpan[1..].SequenceEqual(fin.AsSpan(1))
						: finalSpan.SequenceEqual(fin);
					if (hit) {
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