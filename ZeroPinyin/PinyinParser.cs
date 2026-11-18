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
	/// 搜索端多音节精确音调请使用数字音调形式（如 "yang2mao2"），多音节数据解析请用 <see cref="ParseSyllables"/>。
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

			var (b, t) = MapToneChar(c);

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

	/// <summary>带调字符到（无调字符, 声调）的映射；非带调字符原样返回。</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private (char Char, byte Tone) MapToneChar(char c) => c switch {
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
		'ế' => ('ê', 2), // ê 的带调形式（欸/誒）
		'ề' => ('ê', 4),
		'ể' => ('ê', 3),
		'ệ' => ('ê', 4),
		'ễ' => ('ê', 0),
		_ => (c, 0)
	};

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

		if (input.Length > 1 && input[^1] is >= '1' and <= '5') {
			tone = (byte)(input[^1] - '0');
			toneLen = 1;
		}

		var (ini, fin) = ParsePlain(input[..^toneLen]);
		return fin == 0 ? default : new(ini, fin, tone);
	}

	/// <summary>
	/// 解析可能由多个音节连写的拼音串（如 pinyin-data v0.16.0 中 瓲 "túnwǎ"、注音 ㄊㄨㄣˊㄨㄚ），
	/// 先归一化并把声调记到所修饰字符上，再按"最长音节优先"切分；仅当所有段都是有效音节且每段至多一个声调时成功。
	/// 连读按多个独立音节返回（不生成整读复合键：该读音已弃用，且键化会拉高初始化耗时）；单音节输入返回 1。
	/// </summary>
	/// <param name="input">原始拼音串（可含声调符号、ü、尾部数字声调）。</param>
	/// <param name="syllables">输出缓冲区，容量需不小于音节数（建议至少 16）。</param>
	/// <returns>音节数量；无法完整解析或缓冲区不足时为 0。</returns>
	public static int ParseSyllables(ReadOnlySpan<char> input, Span<PinyinSyllable> syllables) {
		Span<char> chars = stackalloc char[32];
		Span<byte> tones = stackalloc byte[32];

		var length = NormalizeWithTonePositions(input, chars, tones);
		if (length == 0) {
			return 0;
		}

		var count = 0;
		return TrySplit(chars[..length], tones[..length], 0, syllables, ref count) ? count : 0;
	}

	/// <summary>归一化字符并把声调记录到所修饰的字符下标上（<paramref name="tones"/>[i] 为 chars[i] 的声调，0 表示无）。</summary>
	static private int NormalizeWithTonePositions(ReadOnlySpan<char> input, Span<char> chars, Span<byte> tones) {
		var pos = 0;
		foreach (var c in input) {
			if (CombiningTone(c) is var combiningTone and not 0) {
				if (pos == 0) {
					return 0;
				}

				tones[pos - 1] = combiningTone;
				continue;
			}

			if (c is >= '1' and <= '5') {
				if (pos == 0) {
					return 0;
				}

				tones[pos - 1] = (byte)(c - '0');
				continue;
			}

			if (pos >= chars.Length) {
				return 0;
			}

			var (b, t) = MapToneChar(c);
			chars[pos] = b;
			if (t != 0) {
				tones[pos] = t;
			}

			pos++;
		}

		return pos;
	}

	/// <summary>按最长音节优先递归切分，声调取段内所修饰字符的声调。</summary>
	static private bool TrySplit(ReadOnlySpan<char> chars, ReadOnlySpan<byte> tones, int start, Span<PinyinSyllable> syllables, ref int count) {
		if (start == chars.Length) {
			return true;
		}

		if (count >= syllables.Length) {
			return false;
		}

		var maxLen = Math.Min(7, chars.Length - start);
		for (var len = maxLen; len >= 1; len--) {
			byte tone = 0;
			var toneCount = 0;
			for (var i = start; i < start + len; i++) {
				if (tones[i] == 0) {
					continue;
				}

				tone = tones[i];
				toneCount++;
			}

			if (toneCount > 1) {
				continue;
			}

			var (ini, fin) = ParsePlain(chars.Slice(start, len));
			if (fin == 0) {
				continue;
			}

			syllables[count++] = new(ini, fin, tone);
			if (TrySplit(chars, tones, start + len, syllables, ref count)) {
				return true;
			}

			count--;
		}

		return false;
	}

	/// <summary>解析无声调音节（声母 + 韵母），返回声母与韵母下标（韵母为 0 表示失败）。</summary>
	static private (byte Ini, byte Fin) ParsePlain(ReadOnlySpan<char> syl) {
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
						return ((byte)(i + 1), (byte)(j + 1));
					}
				}

				break;
			}
		}

		for (var j = 0; j < FinalList.Length; j++) {
			if (syl.SequenceEqual(FinalList[j])) {
				return (0, (byte)(j + 1));
			}
		}

		return (0, 0);
	}
}
