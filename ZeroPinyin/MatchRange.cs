namespace ZeroPinyin;

/// <summary>
/// 表示一次拼音匹配的文本区间（起始索引与长度）。
/// </summary>
public readonly ref struct MatchRange {
	/// <summary>匹配区间在文本中的起始索引（含）。</summary>
	public readonly int Start;

	/// <summary>匹配区间的长度。</summary>
	public readonly int Length;

	/// <summary>匹配区间的结束索引（不含，即 Start + Length）。</summary>
	public int End => Start + Length;

	/// <summary>
	/// 创建匹配区间。
	/// </summary>
	/// <param name="start">起始索引。</param>
	/// <param name="length">长度。</param>
	public MatchRange(int start, int length) {
		Start = start;
		Length = length;
	}
}
