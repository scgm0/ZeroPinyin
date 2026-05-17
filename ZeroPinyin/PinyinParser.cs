namespace ZeroPinyin;

public static class PinyinParser {
	public static readonly string[] InitList = [
		"zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l",
		"g", "k", "h", "j", "q", "x", "r", "z", "c", "s", "y", "w"
	];

	public static readonly string[] FinalList = [
		"a", "o", "e", "ai", "ei", "ao", "ou", "an", "en", "ang", "eng", "ong", "er",
		"i", "ia", "ie", "iao", "iu", "ian", "in", "iang", "ing", "iong",
		"u", "ua", "uo", "uai", "ui", "uan", "un", "uang", "ueng",
		"v", "ve", "van", "vn"
	];

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