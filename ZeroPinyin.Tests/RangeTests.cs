namespace ZeroPinyin.Tests;

public class RangeTests {
	private readonly PinyinMatcher _matcher = PinyinMatcher.Default;

	[Theory]
	[InlineData("羊毛出在羊身上", "yangmao", 0)]
	[InlineData("一只羊毛", "yangmao", 2)]
	[InlineData("测试文本", "yangmao", -1)]
	[InlineData("", "yangmao", -1)]
	[InlineData("abc", "", 0)]
	public void FindFirstIndex_ShouldReturnCorrectPosition(string text, string search, int expected) {
		Assert.Equal(expected, _matcher.FindFirstIndex(text, search));
	}

	[Fact]
	public void AllMatches_ShouldEnumerateNonOverlappingRanges() {
		var matches = _matcher.AllMatches("羊毛羊毛羊毛", "yangmao");
		Assert.True(matches.MoveNext());
		var (s0, l0) = matches.Current.GetOffsetAndLength(6);
		Assert.Equal(0, s0);
		Assert.Equal(2, l0);
		Assert.True(matches.MoveNext());
		var (s1, _) = matches.Current.GetOffsetAndLength(6);
		Assert.Equal(2, s1);
		Assert.True(matches.MoveNext());
		var (s2, _) = matches.Current.GetOffsetAndLength(6);
		Assert.Equal(4, s2);
		Assert.False(matches.MoveNext());
	}

	[Fact]
	public void AllMatches_ShouldSkipGaps() {
		var matches = _matcher.AllMatches("一只羊毛和羊毛", "yangmao");
		Assert.True(matches.MoveNext());
		Assert.Equal(2, matches.Current.Start.Value);
		Assert.True(matches.MoveNext());
		Assert.Equal(5, matches.Current.Start.Value);
		Assert.False(matches.MoveNext());
	}

	[Fact]
	public void AllMatches_Count_ShouldEqualCountMatches() {
		const string text = "羊毛出在羊身上，羊毛。羊毛布";
		var count = 0;
		var matches = _matcher.AllMatches(text, "yangmao");
		while (matches.MoveNext()) {
			count++;
		}

		Assert.Equal(_matcher.CountMatches(text, "yangmao"), count);
	}

	[Theory]
	[InlineData("羊毛", "yang")]
	[InlineData("中国", "zhongguo")]
	[InlineData("知识", "zisi")]
	public void AllMatches_ShouldProduceValidRanges(string text, string search) {
		var matches = _matcher.AllMatches(text, search);
		while (matches.MoveNext()) {
			var (off, len) = matches.Current.GetOffsetAndLength(text.Length);
			Assert.True(off >= 0);
			Assert.True(len >= 1);
			Assert.True(off + len <= text.Length);
		}
	}

	[Fact]
	public void FindFirstIndex_ShouldMatchContains() {
		foreach (var (text, search) in new[] {
			("羊毛出在羊身上", "yangmao"),
			("测试文本", "yangmao"),
			("中华人民共和国", "zhrmghg"),
			("中国", "zhong国"),
		}) {
			Assert.Equal(_matcher.Contains(text, search), _matcher.FindFirstIndex(text, search) >= 0);
		}
	}

	[Fact]
	public void FindFirstMatch_ShouldReturnNull_WhenNoMatch() {
		Assert.Null(_matcher.FindFirstMatch("测试文本", "yangmao"));
		Assert.Null(_matcher.FindFirstMatch("", "yangmao"));
	}

	[Fact]
	public void FindFirstMatch_ShouldReturnRange_WhenMatched() {
		var r = _matcher.FindFirstMatch("一只羊毛", "yangmao");
		Assert.NotNull(r);
		var (start, length) = r!.Value.GetOffsetAndLength(4);
		Assert.Equal(2, start);
		Assert.Equal(2, length);
	}

	[Fact]
	public void FindFirstMatch_EmptySearch_ShouldReturnZeroRange() {
		var r = _matcher.FindFirstMatch("羊毛", "");
		Assert.NotNull(r);
		Assert.Equal(0, r!.Value.Start.Value);
		Assert.Equal(0, r.Value.End.Value);
	}

	[Fact]
	public void FindFirstMatch_ShouldAlignWithContains() {
		var rnd = new Random(5678);
		foreach (var (text, search) in new[] {
			("羊毛出在羊身上", "yangmao"),
			("测试文本", "yangmao"),
			("中华人民共和国", "zhrmghg"),
			("长江", "zhangjiang"),
			("知识", "zisi"),
		}) {
			Assert.Equal(_matcher.Contains(text, search), _matcher.FindFirstMatch(text, search) is not null);
		}
	}

	[Fact]
	public void AllMatches_ShouldSupportForeach() {
		var count = 0;
		foreach (var r in _matcher.AllMatches("羊毛羊毛", "yangmao")) {
			count++;
			Assert.True(r.End.Value - r.Start.Value >= 1);
		}

		Assert.Equal(2, count);
	}

	[Fact]
	public void Query_AllMatches_ShouldWorkDirectly() {
		var query = _matcher.Compile("mao");
		var matches = query.AllMatches("羊毛和猫毛");
		Assert.True(matches.MoveNext());
		Assert.True(matches.MoveNext());
		Assert.True(matches.MoveNext());
		Assert.False(matches.MoveNext());
	}
}
