namespace ZeroPinyin.Tests;

public class EdgeCaseTests {
	private readonly PinyinMatcher _matcher = PinyinMatcher.Default;

	[Fact]
	public void MaxLength63_ShouldCompile() {
		var query = _matcher.Compile(new string('a', 63));
		Assert.False(query.Contains("羊毛"));
	}

	[Fact]
	public void Length64_ShouldThrow() {
		Assert.Throws<ArgumentException>(() => _matcher.Compile(new string('a', 64)));
	}

	[Theory]
	[InlineData("羊毛", "yang6mao6")]
	[InlineData("羊毛", "yang9mao9")]
	[InlineData("羊毛", "0yangmao")]
	public void InvalidToneDigits_ShouldNotMatch(string text, string search) {
		Assert.False(_matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("！？。，", "yangmao")]
	[InlineData("12345", "yangmao")]
	[InlineData("   ", "yangmao")]
	public void PunctuationOnly_ShouldNotMatch(string text, string search) {
		Assert.False(_matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("", "yangmao", false)]
	[InlineData("", "", true)]
	[InlineData("羊毛", "", true)]
	public void EmptyMatrix(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Fact]
	public void QueryLayer_ShouldWorkWithoutMatcherCache() {
		var map = HanziPinyinMap.Default;
		var config = FuzzyConfig.Default;
		var prefixData = PrefixMapBuilder.Build(map, config);
		var query = new PinyinQuery("yangmao", map, prefixData, config);
		Assert.True(query.Contains("羊毛"));
		Assert.Equal(2, query.CountMatches("羊毛羊毛"));
		Assert.Equal(0, query.FindFirstIndex("羊毛出在羊身上"));
	}

	[Fact]
	public void Polyphone_AllPronunciations_ShouldMatch() {
		foreach (var (text, search) in new[] {
			("重庆", "chongqing"),
			("重庆", "zhongqing"),
			("成长", "chengzhang"),
			("成长", "chengchang"),
			("长江", "changjiang"),
			("长江", "zhangjiang"),
		}) {
			Assert.True(_matcher.Contains(text, search), $"{text} 应匹配 {search}");
		}
	}

	[Fact]
	public void AllMatches_ShouldHandleOverlappingCandidates() {
		var matches = _matcher.AllMatches("aaaa", "aa");
		Assert.True(matches.MoveNext());
		var (s0, l0) = matches.Current.GetOffsetAndLength(4);
		Assert.Equal(0, s0);
		Assert.Equal(2, l0);
		Assert.True(matches.MoveNext());
		Assert.Equal(2, matches.Current.Start.Value);
		Assert.False(matches.MoveNext());
	}

	[Fact]
	public void AllMatches_ShouldWorkWithPolyphoneAndFuzzy() {
		var matches = _matcher.AllMatches("长江长江", "changjiang");
		Assert.True(matches.MoveNext());
		var (s0, l0) = matches.Current.GetOffsetAndLength(4);
		Assert.Equal(0, s0);
		Assert.Equal(2, l0);
		Assert.True(matches.MoveNext());
		Assert.Equal(2, matches.Current.Start.Value);
		Assert.False(matches.MoveNext());

		var fuzzyMatches = _matcher.AllMatches("知识", "zisi");
		Assert.True(fuzzyMatches.MoveNext());
		Assert.Equal(0, fuzzyMatches.Current.Start.Value);
		Assert.Equal(2, fuzzyMatches.Current.GetOffsetAndLength(2).Length);
	}

	[Fact]
	public void AllMatches_ShouldReportPrefixMatchRange() {
		var matches = _matcher.AllMatches("羊毛", "yang");
		Assert.True(matches.MoveNext());
		var (s0, l0) = matches.Current.GetOffsetAndLength(2);
		Assert.Equal(0, s0);
		Assert.Equal(1, l0);
	}

	[Fact]
	public void EmptySearch_AllMatches_ShouldBeEmpty() {
		var matches = _matcher.AllMatches("羊毛", "");
		Assert.False(matches.MoveNext());
	}
}
