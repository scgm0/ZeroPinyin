namespace ZeroPinyin;

public sealed class FuzzyConfig {
	public bool EnableFuzzyInitials { get; set; } = true;
	public bool EnableFuzzyFinals { get; set; } = true;
	public bool ExactMatchForHanzi { get; set; } = true;
	public static FuzzyConfig Default { get; } = new();
}