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
	public void Contains_ShouldNotConfuseLuWithLü(string text, string search) {
		Assert.False(_matcher.Contains(text, search));
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
