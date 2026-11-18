namespace ZeroPinyin.Tests;

public class NormalizationTests {
	private readonly PinyinMatcher _matcher = PinyinMatcher.Default;

	[Theory]
	[InlineData("yáng", "yang2")]
	[InlineData("lüè", "lve4")]
	[InlineData("lǜ", "lv4")]
	[InlineData("nǚ", "nv3")]
	[InlineData("zhōng", "zhong1")]
	[InlineData("yangmao", "yangmao")]
	public void RemoveToneMarks_ShouldNormalize(string input, string expected) {
		Span<char> buf = stackalloc char[64];
		var len = PinyinParser.RemoveToneMarks(input, buf);
		Assert.Equal(expected, buf[..len].ToString());
	}

	[Theory]
	[InlineData("羊毛", "yángmáo")]
	[InlineData("羊毛", "yángmao")]
	[InlineData("羊毛", "yangmáo")]
	[InlineData("绿", "lü")]
	[InlineData("绿", "lǜ")]
	[InlineData("绿", "lv")]
	[InlineData("绿", "lv4")]
	[InlineData("绿色", "lvsè")]
	[InlineData("女", "nǚ")]
	[InlineData("女", "nv")]
	// j/q/x/y 后的 ü 写作 u，v 与 ü 写法均应归一
	[InlineData("学", "xue")]
	[InlineData("学", "xve")]
	[InlineData("学", "xüe")]
	[InlineData("玉", "yv")]
	[InlineData("群", "qvn")]
	public void Contains_ShouldNormalizeUnicodeInput(string text, string search) {
		Assert.True(_matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("羊毛", "yángmáo", "yang2mao2")]
	[InlineData("绿", "lǜ", "lv4")]
	public void NormalizedAndNumbered_ShouldMatchEquivalently(string text, string unicode, string numbered) {
		Assert.True(_matcher.Contains(text, unicode));
		Assert.True(_matcher.Contains(text, numbered));
	}

	[Theory]
	[InlineData("路", "lü")]
	[InlineData("路", "lǜ")]
	[InlineData("禄", "lü4")]
	[InlineData("路", "lue")] // l 后的 üe 仍写作 ve，lue 不等于 lüe
	[InlineData("女", "nu")] // nu 不是 nü
	public void Contains_ShouldNotConfuseLuWithLü(string text, string search) {
		Assert.False(_matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("jve4", "jue4")]  // 输入法 v 写法的数据归一为 u
	[InlineData("qvn2", "qun2")]
	[InlineData("yv4", "yu4")]
	[InlineData("xue2", "xue2")]  // 规范写法保持不变
	[InlineData("lve4", "lve4")]  // l 后的 ü 仍写作 v
	public void Parse_ShouldNormalizeUmlautAfterJqxy(string input, string expected) {
		var syl = PinyinParser.Parse(input);
		Assert.True(syl.IsValid, input);
		Assert.Equal(expected, syl.Numbered);
	}

	[Fact]
	public void DataEnd_UnicodeUmlautChars_ShouldBeParsed() {
		Assert.True(_matcher.Contains("㑼", "lüè"));
		Assert.True(_matcher.Contains("㑼", "lve4"));
		Assert.True(_matcher.Contains("绿", "lv4"));
	}

	[Fact]
	public void AllMatches_ShouldWorkWithNormalizedSearch() {
		var count = 0;
		var matches = _matcher.AllMatches("绿绿绿", "lǜ");
		while (matches.MoveNext()) {
			count++;
		}

		Assert.Equal(3, count);
	}
}
