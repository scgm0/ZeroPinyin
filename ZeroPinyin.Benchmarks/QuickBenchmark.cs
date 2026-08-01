using System.Diagnostics;

namespace ZeroPinyin.Benchmarks;

public static class QuickBenchmark {
	public static void Run() {
		var map = HanziPinyinMap.Default;
		var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Resource", "small.txt"));

		// 1. 冷编译：独立 matcher 每轮编译全新查询，取最小耗时与分配
		{
			var bestUs = double.MaxValue;
			var bestAlloc = long.MaxValue;
			for (var r = 0; r < 5; r++) {
				GC.Collect();
				var compileMatcher = new PinyinMatcher(map);
				var before = GC.GetTotalAllocatedBytes();
				var sw = Stopwatch.StartNew();
				var q = compileMatcher.Compile($"cold{r}");
				sw.Stop();
				bestUs = Math.Min(bestUs, sw.Elapsed.TotalMicroseconds);
				bestAlloc = Math.Min(bestAlloc, GC.GetTotalAllocatedBytes() - before);
				_ = q;
			}

			Console.WriteLine($"ColdCompile_us={bestUs:F0}");
			Console.WriteLine($"ColdCompile_alloc_kb={bestAlloc / 1024}");
		}

		// 2. 热循环：small.txt 逐行匹配，预热 3 轮后取 5 轮最小值
		var hotMatcher = PinyinMatcher.Default;
		Console.WriteLine($"Contains_small_ms={Measure(lines, l => hotMatcher.Contains(l, "yangmao")):F1}");
		Console.WriteLine($"StartsWith_small_ms={Measure(lines, l => hotMatcher.StartsWith(l, "yangmao")):F1}");
		Console.WriteLine($"IsMatch_small_ms={Measure(lines, l => hotMatcher.IsMatch(l, "yangmao")):F1}");

		// 3. 多线程缓存命中：预编译 64 个查询，8 线程各 100k 次命中
		{
			for (var i = 0; i < 64; i++) {
				hotMatcher.Compile($"q_{i}");
			}

			var sw = Stopwatch.StartNew();
			var tasks = new Task[8];
			for (var t = 0; t < tasks.Length; t++) {
				tasks[t] = Task.Run(() => {
					for (var i = 0; i < 100_000; i++) {
						hotMatcher.Compile($"q_{i & 63}");
					}
				});
			}

			Task.WaitAll(tasks);
			sw.Stop();

			Console.WriteLine($"MultiThreadCacheHit_ms={sw.ElapsedMilliseconds}");
		}
	}

	static private double Measure(string[] lines, Func<string, bool> match) {
		for (var i = 0; i < 3; i++) {
			foreach (var line in lines) {
				match(line);
			}
		}

		var best = double.MaxValue;
		for (var r = 0; r < 5; r++) {
			var sw = Stopwatch.StartNew();
			foreach (var line in lines) {
				match(line);
			}

			sw.Stop();
			best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
		}

		return best;
	}
}
