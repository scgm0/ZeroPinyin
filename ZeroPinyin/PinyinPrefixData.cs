namespace ZeroPinyin;

public sealed class PinyinPrefixData(
	ulong[] intKeys,
	PinyinRange[] intRanges,
	ushort[] intValues,
	ulong[] endKeys,
	PinyinRange[] endRanges,
	ushort[] endValues,
	short[] singleCharRanges) {
	public readonly ulong[] IntKeys = intKeys, EndKeys = endKeys;
	public readonly PinyinRange[] IntRanges = intRanges, EndRanges = endRanges;
	public readonly ushort[] IntValues = intValues, EndValues = endValues;
	public readonly short[] SingleCharRanges = singleCharRanges;
}