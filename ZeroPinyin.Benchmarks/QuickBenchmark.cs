using System.Diagnostics;
using System.Runtime.InteropServices;

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

		// 2. 热循环：small.txt 逐行匹配，预热 3 轮后取 5 轮中位数
		var hotMatcher = PinyinMatcher.Default;
		Console.WriteLine($"Env={RuntimeInformation.OSDescription};Cores={Environment.ProcessorCount}");
		Console.WriteLine($"Contains_small_ms={Measure(lines, hotMatcher, "yangmao", 0):F1}");
		Console.WriteLine($"StartsWith_small_ms={Measure(lines, hotMatcher, "yangmao", 1):F1}");
		Console.WriteLine($"IsMatch_small_ms={Measure(lines, hotMatcher, "yangmao", 2):F1}");

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

	static private double Measure(string[] lines, PinyinMatcher matcher, string query, int method) {
		for (var i = 0; i < 3; i++) {
			Run(lines, matcher, query, method);
		}

		var samples = new double[5];
		for (var r = 0; r < 5; r++) {
			var sw = Stopwatch.StartNew();
			Run(lines, matcher, query, method);
			sw.Stop();
			samples[r] = sw.Elapsed.TotalMilliseconds;
		}

		Array.Sort(samples);
		return samples[2];
	}

	static private void Run(string[] lines, PinyinMatcher matcher, string query, int method) {
		foreach (var line in lines) {
			switch (method) {
				case 0:
					matcher.Contains(line, query);
					break;
				case 1:
					matcher.StartsWith(line, query);
					break;
				default:
					matcher.IsMatch(line, query);
					break;
			}
		}
	}
}
