namespace ZeroPinyin.Tests;

public class MatchRangeTests {
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
		Assert.Equal(0, matches.Current.Start);
		Assert.Equal(2, matches.Current.Length);
		Assert.True(matches.MoveNext());
		Assert.Equal(2, matches.Current.Start);
		Assert.True(matches.MoveNext());
		Assert.Equal(4, matches.Current.Start);
		Assert.False(matches.MoveNext());
	}

	[Fact]
	public void AllMatches_ShouldSkipGaps() {
		var matches = _matcher.AllMatches("一只羊毛和羊毛", "yangmao");
		Assert.True(matches.MoveNext());
		Assert.Equal(2, matches.Current.Start);
		Assert.True(matches.MoveNext());
		Assert.Equal(5, matches.Current.Start);
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
			var r = matches.Current;
			Assert.True(r.Start >= 0);
			Assert.True(r.Length >= 1);
			Assert.True(r.End <= text.Length);
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
	public void Query_AllMatches_ShouldWorkDirectly() {
		var query = _matcher.Compile("mao");
		var matches = query.AllMatches("羊毛和猫毛");
		Assert.True(matches.MoveNext());
		Assert.True(matches.MoveNext());
		Assert.True(matches.MoveNext());
		Assert.False(matches.MoveNext());
	}
}
