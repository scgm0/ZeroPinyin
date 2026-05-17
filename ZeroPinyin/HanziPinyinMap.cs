using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeroPinyin;

public sealed class HanziPinyinMap {
	public readonly ushort[] CharToClass = new ushort[65536];
	public readonly PinyinSyllable[] FlatSyllables;
	public readonly PinyinRange[] ClassRanges;
	public readonly int ClassCount;

	public static string DefaultPinyinData {
		get {
			if (field is not null) {
				return field;
			}

			using var stream = typeof(HanziPinyinMap).Assembly.GetManifestResourceStream("ZeroPinyin.pinyin.txt")
				?? throw new InvalidOperationException("未找到嵌入式资源pinyin.txt");
			using var reader = new StreamReader(stream);
			return field = reader.ReadToEnd();
		}
	}
	public static HanziPinyinMap Default { get; } = new(DefaultPinyinData);

	[InlineArray(16)]
	private struct SyllableBuffer {
		private PinyinSyllable _element0;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private record struct SyllableSignature {
		public SyllableBuffer Syllables;
		public byte Count;

		public void Add(PinyinSyllable s) => Syllables[Count++] = s;

		public bool Equals(SyllableSignature other) {
			if (Count != other.Count) {
				return false;
			}

			for (var i = 0; i < Count; i++) {
				if (Syllables[i] != other.Syllables[i]) {
					return false;
				}
			}

			return true;
		}

		public override int GetHashCode() {
			var hash = new HashCode();
			hash.Add(Count);
			for (var i = 0; i < Count; i++) hash.Add(Syllables[i]);
			return hash.ToHashCode();
		}
	}

	public HanziPinyinMap(ReadOnlySpan<char> text) {
		var lineCount = text.Count('\n');
		if (lineCount == 0 && text.Length > 0) {
			lineCount = text.Count('\r');
		}

		lineCount += 1;

		var estimatedUniqueClasses = lineCount / 4 + 256;
		var uniqueSets = new Dictionary<SyllableSignature, ushort>(estimatedUniqueClasses);
		var classRangesList = new List<PinyinRange>(estimatedUniqueClasses + 256);
		var allSyllablesList = new List<PinyinSyllable>(estimatedUniqueClasses * 2);

		for (var i = 0; i < 256; i++) classRangesList.Add(new(0, 0));

		Span<char> sharedBuf = stackalloc char[32];
		var tempSyls = new PinyinSyllable[16];

		foreach (var rawLine in text.EnumerateLines()) {
			var line = rawLine.Trim();
			if (line.IsEmpty || line[0] == '#') {
				continue;
			}

			var commentIdx = line.IndexOf('#');
			if (commentIdx >= 0) {
				line = line[..commentIdx].Trim();
			}

			var colonIdx = line.IndexOf(':');
			if (colonIdx < 0) {
				continue;
			}

			var codePart = line[..colonIdx].Trim();
			if (codePart.Length != 6 || !codePart.StartsWith("U+", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			if (!int.TryParse(codePart[2..], NumberStyles.HexNumber, null, out var cp)) {
				continue;
			}

			if (cp is < 0 or > 0xFFFF) {
				continue;
			}

			var sylCount = 0;
			var remaining = line[(colonIdx + 1)..].Trim();
			while (remaining.Length > 0) {
				var idx = remaining.IndexOf(',');
				var seg = idx < 0 ? remaining : remaining[..idx];
				remaining = idx < 0 ? default : remaining[(idx + 1)..];
				seg = seg.Trim();

				if (seg.IsEmpty || seg.Length > 31) {
					continue;
				}

				var normLen = RemoveToneMarks(seg, sharedBuf);
				var syl = PinyinParser.Parse(sharedBuf[..normLen]);
				if (!syl.IsValid) {
					continue;
				}

				var exists = false;
				for (var i = 0; i < sylCount; i++)
					if (tempSyls[i] == syl) {
						exists = true;
						break;
					}

				if (!exists && sylCount < 16) {
					tempSyls[sylCount++] = syl;
				}
			}

			if (sylCount == 0) {
				continue;
			}

			var activeSpan = tempSyls.AsSpan(0, sylCount);
			activeSpan.Sort();

			var sig = new SyllableSignature();
			for (var i = 0; i < sylCount; i++) sig.Add(activeSpan[i]);

			if (!uniqueSets.TryGetValue(sig, out var classId)) {
				classId = (ushort)classRangesList.Count;
				var offset = allSyllablesList.Count;
				foreach (var s in activeSpan) allSyllablesList.Add(s);
				classRangesList.Add(new(offset, sylCount));
				uniqueSets[sig] = classId;
			}

			CharToClass[cp] = classId;
		}

		for (var i = 0; i < 256; i++) {
			var c = (char)i;
			CharToClass[i] = char.IsAsciiLetter(c) ? char.ToLowerInvariant(c) : (ushort)i;
		}

		FlatSyllables = [.. allSyllablesList];
		ClassRanges = [.. classRangesList];
		ClassCount = classRangesList.Count;
	}

	static private int RemoveToneMarks(ReadOnlySpan<char> input, Span<char> buf) {
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
}