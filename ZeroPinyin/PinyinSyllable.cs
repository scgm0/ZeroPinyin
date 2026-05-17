using System.Runtime.InteropServices;

namespace ZeroPinyin;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PinyinSyllable(byte InitialIdx, byte FinalIdx, byte Tone) : IComparable<PinyinSyllable> {
	public bool IsValid => FinalIdx != 0;
	public string Initial => InitialIdx == 0 ? "" : PinyinParser.InitList[InitialIdx - 1];
	public string Final => FinalIdx == 0 ? "" : PinyinParser.FinalList[FinalIdx - 1];
	public string Plain => string.Concat(Initial, Final);
	public string Numbered => Tone == 0 ? Plain : $"{Initial}{Final}{Tone}";
	public override string ToString() => Numbered;

	public int CompareTo(PinyinSyllable other) {
		var cmp = InitialIdx.CompareTo(other.InitialIdx);
		if (cmp != 0) {
			return cmp;
		}

		cmp = FinalIdx.CompareTo(other.FinalIdx);
		return cmp != 0 ? cmp : Tone.CompareTo(other.Tone);
	}
}