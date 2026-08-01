namespace ZeroPinyin.Tests;

public class PropertyTests {
	private readonly PinyinMatcher _matcher = PinyinMatcher.Default;

	private const string HanziPool = "羊毛出在羊身上中华人民共和国重庆知识中国绿色女一路";
	private const string PinyinPool = "yangmao zhongguo zhrmghg zisi chongqing yang zhong guo mao lv se nv yi lu";

	private static string RandomText(Random rnd, int maxLen) {
		var sb = new System.Text.StringBuilder();
		var len = rnd.Next(1, maxLen + 1);
		for (var i = 0; i < len; i++) {
			sb.Append(HanziPool[rnd.Next(HanziPool.Length)]);
		}

		return sb.ToString();
	}

	private static string RandomSearch(Random rnd) {
		var parts = PinyinPool.Split(' ');
		var sb = new System.Text.StringBuilder();
		var len = rnd.Next(1, 4);
		for (var i = 0; i < len; i++) {
			if (i > 0 && rnd.Next(2) == 0) {
				sb.Append(HanziPool[rnd.Next(HanziPool.Length)]);
			} else {
				sb.Append(parts[rnd.Next(parts.Length)]);
			}
		}

		return sb.ToString();
	}

	[Fact]
	public void FuzzyOn_ShouldBeSupersetOfFuzzyOff() {
		var strict = new PinyinMatcher(HanziPinyinMap.Default, new FuzzyConfig {
			EnableFuzzyInitials = false,
			EnableFuzzyFinals = false,
		});
		var rnd = new Random(42);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 20);
			var search = RandomSearch(rnd);
			Assert.True(
				strict.Contains(text, search) == false || _matcher.Contains(text, search),
				$"模糊关闭匹配时模糊开启必须也匹配: text={text} search={search}");
		}
	}

	[Fact]
	public void LooseHanzi_ShouldBeSupersetOfExactHanzi() {
		var exact = new PinyinMatcher(HanziPinyinMap.Default, new FuzzyConfig {
			ExactMatchForHanzi = true,
		});
		var loose = new PinyinMatcher(HanziPinyinMap.Default, new FuzzyConfig {
			ExactMatchForHanzi = false,
		});
		var rnd = new Random(43);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 20);
			var search = RandomSearch(rnd);
			Assert.True(
				exact.Contains(text, search) == false || loose.Contains(text, search),
				$"精确汉字匹配时匹配则宽松必须也匹配: text={text} search={search}");
		}
	}

	[Fact]
	public void Contains_ShouldBeMonotonicUnderOuterExtension() {
		var rnd = new Random(44);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 10);
			var search = RandomSearch(rnd);
			if (_matcher.Contains(text, search)) {
				var extended = "路" + text + "女";
				Assert.True(_matcher.Contains(extended, search),
					$"外层扩展不得破坏匹配: text={text} search={search}");
			}
		}
	}

	[Fact]
	public void StartsWithAndEndsWith_ShouldImplyContains() {
		var rnd = new Random(45);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 10);
			var search = RandomSearch(rnd);
			Assert.True(
				_matcher.StartsWith(text, search) == false || _matcher.Contains(text, search),
				$"StartsWith 必须蕴含 Contains: text={text} search={search}");
			Assert.True(
				_matcher.EndsWith(text, search) == false || _matcher.Contains(text, search),
				$"EndsWith 必须蕴含 Contains: text={text} search={search}");
		}
	}

	[Fact]
	public void CaseInsensitivity_ShouldHold() {
		var rnd = new Random(46);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 10);
			var search = RandomSearch(rnd).ToUpperInvariant();
			Assert.True(
				_matcher.Contains(text, search) == _matcher.Contains(text, search.ToLowerInvariant()),
				$"大小写必须不敏感: text={text} search={search}");
		}
	}

	[Fact]
	public void CountMatches_ShouldNotExceedTextLength() {
		var rnd = new Random(48);
		for (var i = 0; i < 200; i++) {
			var text = RandomText(rnd, 10);
			var search = RandomSearch(rnd);
			Assert.True(_matcher.CountMatches(text, search) <= text.Length,
				$"匹配数不得超过文本长度: text={text} search={search}");
		}
	}
}
