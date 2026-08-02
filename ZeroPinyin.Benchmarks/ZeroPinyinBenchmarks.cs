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
	public required string Query { get; set; }

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

	[Benchmark]
	public int FindFirstIndex() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			if (matcher.FindFirstIndex(line, query) >= 0) count++;
		}

		return count;
	}

	[Benchmark]
	public int FindFirstMatch() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			if (matcher.FindFirstMatch(line, query) is not null) count++;
		}

		return count;
	}

	[Benchmark]
	public int AllMatches() {
		var count = 0;
		var lines = _lines;
		var query = Query;
		var matcher = _matcher;

		foreach (var line in lines) {
			var matches = matcher.AllMatches(line, query);
			while (matches.MoveNext()) {
				count++;
			}
		}

		return count;
	}

	private int _coldCounter;
	private bool _hotQueriesReady;
	private readonly string[] _hotQueries = new string[64];

	[Benchmark]
	public PinyinQuery ColdCompile() {
		return _matcher.Compile($"cold{_coldCounter++}");
	}

	[Benchmark]
	public long MultiThreadCacheHit() {
		var matcher = _matcher;
		var queries = _hotQueries;
		if (!_hotQueriesReady) {
			for (var i = 0; i < queries.Length; i++) {
				queries[i] = $"q_{i}";
				matcher.Compile(queries[i]);
			}

			_hotQueriesReady = true;
		}

		var results = new long[8];
		var threads = new Thread[8];
		for (var t = 0; t < threads.Length; t++) {
			var idx = t;
			threads[t] = new(() => {
				var n = 0L;
				for (var i = 0; i < 100_000; i++) {
					var q = matcher.Compile(queries[i & 63]);
					n += q.SearchText.Length;
				}

				results[idx] = n;
			});
			threads[t].Start();
		}

		foreach (var thread in threads) {
			thread.Join();
		}

		var total = 0L;
		foreach (var r in results) {
			total += r;
		}

		return total;
	}
}