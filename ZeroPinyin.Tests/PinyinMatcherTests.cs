namespace ZeroPinyin.Tests;

public class PinyinMatcherTests {
	private readonly PinyinMatcher _matcher = PinyinMatcher.Default;

	#region 1. 基础匹配模式测试

	[Theory]
	[InlineData("我是羊毛", "yangmao", true)]
	[InlineData("羊毛出在羊身上", "yangmao", true)]
	[InlineData("羊肉出在羊身上", "yangmao", false)]
	[InlineData("Hello世界", "helloshi", true)]
	public void Contains_ShouldMatchCorrectly(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("羊毛", "yang", true)]
	[InlineData("薅羊毛", "yang", false)]
	[InlineData("羊毛", "羊", true)]
	[InlineData("abc", "ab", true)]
	[InlineData("abc", "bc", false)]
	public void StartsWith_ShouldMatchCorrectly(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.StartsWith(text, search));
	}

	[Theory]
	[InlineData("薅羊毛", "mao", true)]
	[InlineData("羊毛布", "mao", false)]
	[InlineData("薅羊毛", "毛", true)]
	[InlineData("abc", "bc", true)]
	[InlineData("abc", "ab", false)]
	public void EndsWith_ShouldMatchCorrectly(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.EndsWith(text, search));
	}

	[Theory]
	[InlineData("羊毛", "yangmao", true)]
	[InlineData("羊毛", "羊毛", true)]
	[InlineData("abc", "abc", true)]
	[InlineData("羊毛布", "yangmao", false)]
	[InlineData("", "anything", false)]
	[InlineData("", "", true)]
	public void IsMatch_ShouldMatchCorrectly(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.IsMatch(text, search));
	}

	[Theory]
	[InlineData("羊毛羊毛", "yangmao", 2)]
	[InlineData("羊毛羊毛", "羊毛", 2)]
	[InlineData("羊毛出在羊身上", "yang", 2)]
	[InlineData("ababab", "ab", 3)]
	[InlineData("abc", "d", 0)]
	[InlineData("aaaa", "aa", 2)] // 状态机会在匹配后重置，不会计算重叠部分
	public void CountMatches_ShouldReturnCorrectCount(string text, string search, int expectedCount) {
		Assert.Equal(expectedCount, _matcher.CountMatches(text, search));
	}

	#endregion

	#region 2. 高级特性测试

	[Theory]
	[InlineData("中国", "zg", true)] // 首字母缩写匹配
	[InlineData("中华人民共和国", "zhrmghg", true)]
	[InlineData("中华人民共和国", "renmin", true)] // 中间词全拼匹配
	[InlineData("羊毛党", "ymd", true)]
	public void Search_ShouldSupportAcronymsAndPrefixes(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("羊毛党", "yAngMaO", true)]
	[InlineData("HelloWorld", "helloworld", true)]
	[InlineData("GITHUB", "github", true)]
	public void Search_ShouldBeCaseInsensitive(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("成长", "chengzhang", true)]
	[InlineData("成长", "chengchang", true)] // 长(zhang/chang)
	[InlineData("长江", "changjiang", true)]
	[InlineData("长江", "zhangjiang", true)]
	[InlineData("重庆", "chongqing", true)]
	[InlineData("重庆", "zhongqing", true)] // 重(chong/zhong)
	public void Search_ShouldSupportPolyphones(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("羊毛", "yang2mao2", true)] // 完全精准带音调
	[InlineData("羊毛", "yang2mao", true)] // 部分带音调
	[InlineData("羊毛", "yang1mao2", false)] // 音调错误应拒绝匹配
	[InlineData("羊毛", "yang2mao3", false)]
	public void Search_ShouldSupportToneNumbers(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("中国", "zhong国", true)] // 拼音汉字混拼
	[InlineData("数字123", "123", true)] // ASCII数字穿插
	[InlineData("测试!", "test!", false)] // 搜索串中有特殊符号时，如果原串没有，则不匹配
	[InlineData("测试!", "测试!", true)] // 精确匹配原字符
	public void Search_ShouldSupportMixedChineseAndASCII(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	#endregion

	#region 3. 模糊音测试

	[Theory]
	[InlineData("知识", "zisi", true)] // zh/z, sh/s
	[InlineData("吃饭", "cifan", true)] // ch/c
	[InlineData("上海", "sanghai", true)] // sh/s
	[InlineData("牛奶", "liunai", true)] // n/l
	[InlineData("老虎", "naohu", true)] // l/n
	[InlineData("发挥", "hahui", true)] // f/h
	[InlineData("福建", "hujian", true)] // f/h
	[InlineData("软体", "luan", true)] // r/l
	public void Fuzzy_ShouldMatchInitials(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Theory]
	[InlineData("安全", "angquan", true)] // an/ang
	[InlineData("肮脏", "anzang", true)] // ang/an
	[InlineData("根本", "gengben", true)] // en/eng
	[InlineData("更好", "genhao", true)] // eng/en
	[InlineData("拼音", "pingying", true)] // in/ing
	[InlineData("苹果", "pinguo", true)] // ing/in
	[InlineData("前面", "qiangmiang", true)] // ian/iang
	[InlineData("坚强", "jiangqian", true)] // iang/ian
	[InlineData("端正", "duangzheng", true)] // uan/uang
	public void Fuzzy_ShouldMatchFinals(string text, string search, bool expected) {
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Fact]
	public void Fuzzy_CustomConfig_ShouldRespectSettings() {
		// 测试关闭所有模糊音
		var fuzzyOff = new FuzzyConfig { EnableFuzzyInitials = false, EnableFuzzyFinals = false };
		var strictMatcher = new PinyinMatcher(HanziPinyinMap.Default, fuzzyOff);

		Assert.True(strictMatcher.Contains("知识", "zhishi"));
		Assert.False(strictMatcher.Contains("知识", "zisi")); // z/zh 模糊音失效

		Assert.True(strictMatcher.Contains("安全", "anquan"));
		Assert.False(strictMatcher.Contains("安全", "angquan")); // an/ang 模糊音失效
	}

	#endregion

	#region 4. 其他机制测试

	[Fact]
	public void EdgeCase_EmptyStrings() {
		Assert.False(_matcher.Contains("", "a"));
		Assert.True(_matcher.Contains("a", ""));
		Assert.False(_matcher.StartsWith("", "a"));
		Assert.True(_matcher.StartsWith("a", ""));
		Assert.True(_matcher.EndsWith("a", ""));
		Assert.Equal(0, _matcher.CountMatches("", "a"));
		Assert.Equal(0, _matcher.CountMatches("a", ""));
	}

	[Fact]
	public void EdgeCase_LongSearchString() {
		// 超过 ulong 状态机位数 (64) 应该抛出异常
		var longPinyin = new string('a', 64);
		Assert.Throws<ArgumentException>(() => _matcher.Compile(longPinyin));

		// 最大支持 63 位并行匹配
		var validLong = new string('a', 63);
		var query = _matcher.Compile(validLong);
		Assert.NotNull(query);
		Assert.False(query.Contains("text"));
	}

	[Fact]
	public void Cache_ShouldReturnSameInstanceForSameQuery() {
		var first = _matcher.Compile("test");
		var second = _matcher.Compile("test");
		Assert.Same(first, second); // 同一个关键词，应该直出缓存对象

		var third = _matcher.Compile("different");
		Assert.NotSame(first, third);
	}

	[Fact]
	public void Cache_FIFOEviction_ShouldEvictOldestItems() {
		const int cacheSize = 1024;

		// 塞满缓存池并触发淘汰
		for (var i = 0; i < cacheSize + 5; i++) {
			_matcher.Compile($"pinyin_{i}");
		}

		// pinyin_0 已经被淘汰，所以编译出来的对象是一个全新分配的实例
		var early = _matcher.Compile("pinyin_0");
		var recompiled = _matcher.Compile("pinyin_0");
		Assert.Same(early, recompiled);
	}

	#endregion

	#region 5. 中文精确匹配测试 (ExactMatchForHanzi)

	[Theory]
	[InlineData("中国", "中国", true)]
	[InlineData("中国", "中guo", true)] // 汉字拼音混合，'中'字精确匹配，'国'走拼音匹配
	[InlineData("中华人民共和国", "中华rmghg", true)]
	[InlineData("忠帼", "中国", false)] // 即使拼音都是 'zhong guo'，但 '忠' != '中'，'帼' != '国'
	[InlineData("忠帼", "zhong国", false)] // 搜索词包含了汉字'国'，必须精确匹配'国'
	[InlineData("忠帼", "中guo", false)] // 搜索词包含了汉字'中'，必须精确匹配'中'
	public void ExactMatchForHanzi_DefaultTrue_ShouldRejectHomophones(string text, string search, bool expected) {
		// 默认配置下，搜索词中出现的汉字必须精确匹配，拒绝隐式的同音字映射
		Assert.Equal(expected, _matcher.Contains(text, search));
	}

	[Fact]
	public void ExactMatchForHanzi_False_ShouldAllowHomophones() {
		// 测试关闭中文字符精确匹配：会将搜索词中的中文作为其拼音处理（等同于隐式同音字）
		var config = new FuzzyConfig { ExactMatchForHanzi = false };
		var looseMatcher = new PinyinMatcher(HanziPinyinMap.Default, config);

		Assert.True(looseMatcher.Contains("忠帼", "中国")); // "忠帼" 的拼音和 "中国" 相同，允许匹配
		Assert.True(looseMatcher.Contains("忠帼", "zhong国"));
		Assert.True(looseMatcher.Contains("羊矛", "羊毛")); // "矛" 的拼音和 "毛" 相同，允许匹配
	}

	#endregion

}