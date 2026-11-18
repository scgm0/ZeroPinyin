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
	[InlineData("ḿ", "m2")]   // 成音节鼻音
	[InlineData("ňg", "ng3")]
	[InlineData("m\u0300", "m4")] // m + 组合重音符
	[InlineData("n\u0304", "n1")] // n + 组合长音符
	[InlineData("ế", "ê2")]       // ê 的带调形式（诶/誒）
	[InlineData("ề", "ê4")]
	[InlineData("ê\u0304", "ê1")]  // ê + 组合长音符
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

	[Theory]
	[InlineData("n4", "", "n", "n4")]
	[InlineData("ng2", "", "ng", "ng2")]
	[InlineData("hng", "h", "ng", "hng")]
	[InlineData("hm", "h", "m", "hm")]
	[InlineData("ê", "", "ê", "ê")]
	[InlineData("ê2", "", "ê", "ê2")]
	public void Parse_SyllabicNasal_ShouldBeValid(string input, string initial, string final, string numbered) {
		var syl = PinyinParser.Parse(input);
		Assert.True(syl.IsValid, input);
		Assert.Equal(initial, syl.Initial);
		Assert.Equal(final, syl.Final);
		Assert.Equal(numbered, syl.Numbered);
	}

	[Fact]
	public void DataEnd_UnicodeUmlautChars_ShouldBeParsed() {
		Assert.True(_matcher.Contains("㑼", "lüè"));
		Assert.True(_matcher.Contains("㑼", "lve4"));
		Assert.True(_matcher.Contains("绿", "lv4"));
	}

	[Theory]
	[InlineData("嗯", "n")]
	[InlineData("嗯", "ng")]
	[InlineData("嗯", "n2")]  // ń
	[InlineData("嗯", "ǹ")]   // ǹ → n4
	[InlineData("嗯", "n4")]
	[InlineData("㕶", "n")]
	[InlineData("㕶", "ňg")]  // ňg → ng3
	[InlineData("呣", "m")]
	[InlineData("哼", "hng")]
	[InlineData("噷", "hm")]
	public void SyllabicNasal_ShouldMatch(string text, string search) {
		Assert.True(_matcher.Contains(text, search), $"{text} 应匹配 {search}");
		Assert.True(_matcher.FindFirstIndex(text, search) >= 0, $"{text} 应能定位 {search}");
	}

	[Fact]
	public void SyllabicNasal_HanziItself_ShouldMatch() {
		Assert.Equal(new Range(0, 1), _matcher.FindFirstMatch("嗯", "嗯"));
		Assert.Equal(new Range(0, 1), _matcher.FindFirstMatch("㕶", "㕶"));
	}

	[Theory]
	[InlineData("嗯", "ni")]
	[InlineData("嗯", "na")]
	[InlineData("㕶", "nu")]
	public void SyllabicNasal_ShouldNotMatchOtherSyllables(string text, string search) {
		Assert.False(_matcher.Contains(text, search), $"{text} 不应匹配 {search}");
	}

	[Theory]
	[InlineData("欸", "ê")]
	[InlineData("欸", "ê̄")]
	[InlineData("欸", "ế")]
	[InlineData("欸", "ề")]
	[InlineData("欸", "ê1")]
	[InlineData("欸", "ê2")]
	[InlineData("欸", "ê3")]
	[InlineData("誒", "ê")]
	[InlineData("誒", "ề")]
	public void ECircumflexSyllable_ShouldMatch(string text, string search) {
		Assert.True(_matcher.Contains(text, search), $"{text} 应匹配 {search}");
		Assert.Equal(new Range(0, 1), _matcher.FindFirstMatch(text, search));
	}

	[Fact]
	public void ECircumflex_HanziItself_ShouldMatch() {
		Assert.Equal(new Range(0, 1), _matcher.FindFirstMatch("欸", "欸"));
	}

	[Theory]
	[InlineData("三", "ê")]
	[InlineData("欸", "ê5")]
	public void ECircumflex_ShouldNotMatchOtherSyllables(string text, string search) {
		Assert.False(_matcher.Contains(text, search), $"{text} 不应匹配 {search}");
	}

	[Theory]
	[InlineData("xue2", 1)]
	[InlineData("xue", 1)]
	[InlineData("zhōng", 1)]
	[InlineData("túnwǎ", 2)] // 连读（注音 ㄊㄨㄣˊㄨㄚ）
	[InlineData("tun2wa3", 2)]
	[InlineData("tun2wa3zhi4", 3)]
	[InlineData("zzz", 0)]
	[InlineData("tun2zzz3", 0)]
	public void ParseSyllables_ShouldSplitContinuousReading(string input, int expected) {
		Span<PinyinSyllable> buf = stackalloc PinyinSyllable[16];
		Assert.Equal(expected, PinyinParser.ParseSyllables(input, buf));

		if (input == "túnwǎ") {
			Assert.Equal("tun2", buf[0].Numbered); // 每个音节的声调需各自保留
			Assert.Equal("wa3", buf[1].Numbered);
		}
	}

	[Theory]
	[InlineData("瓲", "tun")] // 连读（ㄊㄨㄣˊㄨㄚ）拆开后的各音节
	[InlineData("瓲", "wa")]
	[InlineData("瓲", "tun2")]
	[InlineData("瓲", "wa3")]
	public void ContinuousReading_ShouldExposeEachSyllable(string text, string search) {
		Assert.True(_matcher.Contains(text, search), $"{text} 应匹配 {search}");
		Assert.Equal(new Range(0, 1), _matcher.FindFirstMatch(text, search));
	}

	[Theory]
	[InlineData("瓲", "tunwa")]  // 整读键不做支持：连续发音已弃用，键化会拉高初始化耗时
	[InlineData("瓲", "tun2wa3")]
	[InlineData("㕶", "tunwa")]
	public void ContinuousReading_WholeReading_ShouldNotMatch(string text, string search) {
		Assert.False(_matcher.Contains(text, search), $"{text} 不应匹配 {search}");
	}

	[Fact]
	public void IsNormalizableChar_ShouldMatchRemoveToneMarks() {
		// 防漂移：凡 RemoveToneMarks 会改变结果的字符，必须被 IsNormalizableChar 识别（否则搜索串不会触发归一化）
		Span<char> buf = stackalloc char[2];
		for (var i = 0; i < 0x10000; i++) {
			var c = (char)i;
			var len = PinyinParser.RemoveToneMarks([c], buf);
			var changed = len != 1 || buf[0] != c;
			Assert.True(changed == PinyinParser.IsNormalizableChar(c),
				$"U+{(int)c:X4} changed={changed} out=[{(len == 1 ? buf[0].ToString() : buf[..len].ToString())}]");
		}
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
