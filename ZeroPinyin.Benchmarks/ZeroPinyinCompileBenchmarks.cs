using BenchmarkDotNet.Attributes;

namespace ZeroPinyin.Benchmarks;

[Config(typeof(CustomBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 10)]
public class ZeroPinyinCompileBenchmarks {
	private PinyinMatcher _matcher = null!;
	private int _coldCounter;
	private readonly string[] _hotQueries = new string[64];

	[GlobalSetup]
	public void Setup() {
		_matcher = PinyinMatcher.Default;
	}

	[Benchmark]
	public PinyinQuery ColdCompile() {
		return _matcher.Compile($"cold{_coldCounter++}");
	}

	[Benchmark]
	public long MultiThreadCacheHit() {
		var matcher = _matcher;
		var queries = _hotQueries;
		if (queries[0] is null) {
			for (var i = 0; i < queries.Length; i++) {
				queries[i] = $"q_{i}";
				matcher.Compile(queries[i]);
			}
		}

		var tasks = new Task<long>[8];
		for (var t = 0; t < tasks.Length; t++) {
			tasks[t] = Task.Run(() => {
				var n = 0L;
				for (var i = 0; i < 100_000; i++) {
					var q = matcher.Compile(queries[i & 63]);
					n += q.SearchText.Length;
				}
				return n;
			});
		}

		Task.WaitAll(tasks);
		var total = 0L;
		foreach (var task in tasks) total += task.Result;
		return total;
	}
}
