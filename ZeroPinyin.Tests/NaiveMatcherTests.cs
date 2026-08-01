namespace ZeroPinyin.Tests;

public class NaiveMatcherTests {
	static private readonly string[] SyllablePool = [
		"yang", "mao", "zhong", "guo", "ren", "min", "zhi", "shi", "chang", "jiang",
		"chong", "qing", "cheng", "zhang", "lv", "se", "nv", "yi", "lu", "xue",
	];

	private const string HanziPool = "羊毛出在羊身上中华人民共和国重庆知识中国绿色女一路长江成长";

	static private string RandomText(Random rnd, int maxLen) {
		var sb = new System.Text.StringBuilder();
		var len = rnd.Next(1, maxLen + 1);
		for (var i = 0; i < len; i++) {
			sb.Append(HanziPool[rnd.Next(HanziPool.Length)]);
		}

		return sb.ToString();
	}

	static private string RandomPinyinSearch(Random rnd) {
		var sb = new System.Text.StringBuilder();
		var sylCount = rnd.Next(1, 4);
		for (var i = 0; i < sylCount; i++) {
			var syl = SyllablePool[rnd.Next(SyllablePool.Length)];
			var cut = rnd.Next(1, Math.Min(4, syl.Length) + 1);
			sb.Append(syl[..cut]);
			if (rnd.Next(4) == 0) {
				sb.Append((char)('0' + rnd.Next(1, 6)));
			}

			if (rnd.Next(4) == 0) {
				var upper = sb.ToString().ToUpperInvariant();
				sb.Clear();
				sb.Append(upper);
			}
		}

		return sb.ToString();
	}

	[Fact]
	public void Contains_ShouldAgreeWithNaive_OnStrictConfig() {
		var strict = new PinyinMatcher(HanziPinyinMap.Default, new FuzzyConfig {
			EnableFuzzyInitials = false,
			EnableFuzzyFinals = false,
		});
		var rnd = new Random(1234);
		for (var i = 0; i < 500; i++) {
			var text = RandomText(rnd, 8);
			var search = RandomPinyinSearch(rnd);
			var expected = NaiveMatcher.Contains(text, search);
			var actual = strict.Contains(text, search);
			Assert.True(expected == actual, $"朴素与引擎不一致: text={text} search={search} naive={expected} engine={actual}");
		}
	}

	[Fact]
	public void HanziSearch_ShouldAgreeWithNaive() {
		var strict = new PinyinMatcher(HanziPinyinMap.Default, new FuzzyConfig {
			EnableFuzzyInitials = false,
			EnableFuzzyFinals = false,
		});
		var rnd = new Random(2345);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 8);
			var search = RandomText(rnd, 3);
			var expected = NaiveMatcher.Contains(text, search);
			Assert.Equal(expected, strict.Contains(text, search));
		}
	}

	[Fact]
	public void FindFirstIndex_ShouldAgreeWithNaive() {
		var strict = new PinyinMatcher(HanziPinyinMap.Default, new FuzzyConfig {
			EnableFuzzyInitials = false,
			EnableFuzzyFinals = false,
		});
		var rnd = new Random(3456);
		for (var i = 0; i < 300; i++) {
			var text = RandomText(rnd, 8);
			var search = RandomPinyinSearch(rnd);
			var naive = NaiveMatcher.Contains(text, search);
			var idx = strict.FindFirstIndex(text, search);
			Assert.True(naive == (idx >= 0), $"FindFirstIndex 与朴素不一致: text={text} search={search} naive={naive} idx={idx}");
			if (naive) {
				Assert.InRange(idx, 0, text.Length - 1);
			}
		}
	}

	[Theory]
	[InlineData("羊毛", "yangmao")]
	[InlineData("中国", "zhongguo")]
	[InlineData("重庆", "chongqing")]
	[InlineData("绿色", "lvse4")]
	public void Naive_ShouldMatchKnownCases(string text, string search) {
		Assert.True(NaiveMatcher.Contains(text, search));
	}
}
