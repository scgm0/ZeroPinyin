namespace ZeroPinyin;

/// <summary>拼音匹配的模糊配置（声母/韵母模糊音、汉字精确匹配开关）。</summary>
public sealed class FuzzyConfig {
	/// <summary>是否启用声母模糊音（如 zh/z、sh/s、n/l、f/h 等）。默认开启。</summary>
	public bool EnableFuzzyInitials { get; init; } = true;

	/// <summary>是否启用韵母模糊音（如 an/ang、in/ing、en/eng 等）。默认开启。</summary>
	public bool EnableFuzzyFinals { get; init; } = true;

	/// <summary>搜索串中的汉字是否要求精确字符匹配。默认开启；关闭后按同音字匹配。</summary>
	public bool ExactMatchForHanzi { get; init; } = true;

	/// <summary>默认模糊配置（开启声母/韵母模糊音与汉字精确匹配）。</summary>
	public static FuzzyConfig Default { get; } = new();
}