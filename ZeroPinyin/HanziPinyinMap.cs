using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeroPinyin;

/// <summary>汉字到拼音音节集合的映射表（字符 → 音节组合类），由拼音数据构建。</summary>
public sealed class HanziPinyinMap {
	/// <summary>字符到音节组合类的映射（65536 项，0 表示无拼音）。</summary>
	public readonly ushort[] CharToClass = new ushort[65536];
	/// <summary>所有音节的扁平存储（按类区间组织）。</summary>
	public readonly PinyinSyllable[] FlatSyllables;
	/// <summary>每类的音节区间（前 256 项为 ASCII 字符类）。</summary>
	public readonly PinyinRange[] ClassRanges;
	/// <summary>音节组合类的总数。</summary>
	public readonly int ClassCount;

	/// <summary>内置拼音数据（pinyin-data，约 4.4 万汉字）。</summary>
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
	/// <summary>使用内置拼音数据构建的默认映射表（单例）。</summary>
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

	/// <summary>
	/// 从拼音数据文本构建映射表。每行格式："U+4E2D: zhong1,zhong4" 或 "U+4E2D: zhōng,zhòng"（支持声调符号与 ü）。
	/// </summary>
	/// <param name="text">拼音数据文本。</param>
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

				var normLen = PinyinParser.RemoveToneMarks(seg, sharedBuf);
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
}
