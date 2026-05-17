using BenchmarkDotNet.Attributes;

namespace ZeroPinyin.Benchmarks;

[Config(typeof(CustomBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 10)]
public class ZeroPinyinBenchmarks {
	private PinyinMatcher _matcher = null!;
	private string[] _lines = null!;

	[ParamsSource(nameof(GetTestFiles))]
	public TestFileParam FileParam { get; set; } = null!;

	public IEnumerable<TestFileParam> GetTestFiles() {
		yield return new("small.txt");
		yield return new("large.txt");
	}

	[Params("yangmao")]
	public string Query { get; set; } = "yangmao";

	[GlobalSetup]
	public void Setup() {
		_matcher = PinyinMatcher.Default;
		_lines = FileParam.Lines;
	}

	[Benchmark]
	public PinyinMatcher Init() {
		var map = new HanziPinyinMap(HanziPinyinMap.DefaultPinyinData);
		var matcher = new PinyinMatcher(map);
		return matcher;
	}

	[Benchmark]
	public int Contains() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			if (matcher.Contains(line, query)) count++;
		}

		return count;
	}

	[Benchmark]
	public int CountMatches() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			count += matcher.CountMatches(line, query);
		}

		return count;
	}

	[Benchmark]
	public int StartsWith() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			if (matcher.StartsWith(line, query)) count++;
		}

		return count;
	}

	[Benchmark]
	public int EndsWith() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			if (matcher.EndsWith(line, query)) count++;
		}

		return count;
	}

	[Benchmark]
	public int IsMatch() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			if (matcher.IsMatch(line, query)) count++;
		}

		return count;
	}
}