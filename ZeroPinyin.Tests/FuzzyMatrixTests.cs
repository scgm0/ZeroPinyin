namespace ZeroPinyin.Tests;

public class FuzzyMatrixTests {
	private readonly PinyinMatcher _fuzzy = PinyinMatcher.Default;
	private readonly PinyinMatcher _strict = new(HanziPinyinMap.Default, new FuzzyConfig {
		EnableFuzzyInitials = false,
		EnableFuzzyFinals = false,
	});

	[Theory]
	// 声母模糊组
	[InlineData("三", "shan")]  // s/sh
	[InlineData("山", "san")]
	[InlineData("是", "si")]
	[InlineData("在", "zhai")]  // z/zh
	[InlineData("唱", "cang")]  // c/ch
	[InlineData("人", "len")]   // r/l
	// 韵母模糊组
	[InlineData("占", "zhang")] // an/ang (zhan ↔ zhang)
	[InlineData("针", "zheng")] // en/eng
	[InlineData("心", "xing")]  // in/ing
	public void FuzzyOn_ShouldMatch_WhenFuzzyOffDoesNot(string text, string search) {
		Assert.True(_fuzzy.Contains(text, search), $"模糊开启应匹配: {text} {search}");
		Assert.False(_strict.Contains(text, search), $"模糊关闭不应匹配: {text} {search}");
	}

	[Theory]
	[InlineData("三", "san")]
	[InlineData("是", "shi")]
	[InlineData("在", "zai")]
	[InlineData("唱", "chang")]
	[InlineData("忙", "mang")]
	[InlineData("针", "zhen")]
	[InlineData("心", "xin")]
	public void ExactSyllable_ShouldMatch_UnderBothConfigs(string text, string search) {
		Assert.True(_fuzzy.Contains(text, search));
		Assert.True(_strict.Contains(text, search));
	}

	[Fact]
	public void FuzzyOff_ShouldRejectFuzzyOnlyMatch() {
		Assert.False(_strict.Contains("知识", "zisi"));
	}

	[Fact]
	public void ExactToneMatch_ShouldWorkUnderStrictConfig() {
		Assert.True(_strict.Contains("羊毛", "yang2mao2"));
	}
}
