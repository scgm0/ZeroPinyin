using System.Runtime.InteropServices;

namespace ZeroPinyin;

/// <summary>一个拼音音节（声母索引、韵母索引与声调），以紧凑二进制布局存储。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PinyinSyllable(byte InitialIdx, byte FinalIdx, byte Tone) : IComparable<PinyinSyllable> {
	/// <summary>是否为有效音节（韵母索引非零）。</summary>
	public bool IsValid => FinalIdx != 0;
	/// <summary>声母文本（无声母时为空字符串）。</summary>
	public string Initial => InitialIdx == 0 ? "" : PinyinParser.InitList[InitialIdx - 1];
	/// <summary>韵母文本。</summary>
	public string Final => FinalIdx == 0 ? "" : PinyinParser.FinalList[FinalIdx - 1];
	/// <summary>无声调形式的拼音（声母 + 韵母）。</summary>
	public string Plain => string.Concat(Initial, Final);
	/// <summary>带数字声调形式的拼音（如 "yang2"）。</summary>
	public string Numbered => Tone == 0 ? Plain : $"{Initial}{Final}{Tone}";
	/// <summary>返回带数字声调的拼音文本。</summary>
	public override string ToString() => Numbered;

	/// <summary>按声母、韵母、声调依次比较两个音节。</summary>
	public int CompareTo(PinyinSyllable other) {
		var cmp = InitialIdx.CompareTo(other.InitialIdx);
		if (cmp != 0) {
			return cmp;
		}

		cmp = FinalIdx.CompareTo(other.FinalIdx);
		return cmp != 0 ? cmp : Tone.CompareTo(other.Tone);
	}
}