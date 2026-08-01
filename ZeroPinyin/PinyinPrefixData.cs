namespace ZeroPinyin;

/// <summary>拼音前缀索引数据：编码键到音节类列表的映射，用于查询编译时的二分查找。</summary>
public sealed class PinyinPrefixData(
	ulong[] intKeys,
	PinyinRange[] intRanges,
	ushort[] intValues,
	ulong[] endKeys,
	PinyinRange[] endRanges,
	ushort[] endValues,
	short[] singleCharRanges) {
	/// <summary>中间位置前缀的编码键（升序）。</summary>
	public readonly ulong[] IntKeys = intKeys, EndKeys = endKeys;
	/// <summary>中间位置前缀键对应的类区间。</summary>
	public readonly PinyinRange[] IntRanges = intRanges, EndRanges = endRanges;
	/// <summary>类区间内的类 ID 列表。</summary>
	public readonly ushort[] IntValues = intValues, EndValues = endValues;
	/// <summary>单字符前缀到键索引的直接映射（O(1) 查表，-1 表示不存在）。</summary>
	public readonly short[] SingleCharRanges = singleCharRanges;
}