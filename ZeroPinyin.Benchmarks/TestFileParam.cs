namespace ZeroPinyin.Benchmarks;

public class TestFileParam {
	public string FileName { get; }
	public string[] Lines { get; }
	public string SizeStr { get; }

	public TestFileParam(string fileName) {
		FileName = fileName;
		var filePath = Path.Combine(AppContext.BaseDirectory, "Resource", fileName);

		if (File.Exists(filePath)) {
			var fileInfo = new FileInfo(filePath);
			var mib = fileInfo.Length / 1024.0 / 1024.0;
			SizeStr = mib >= 1 ? $"{mib:F1} MiB" : $"{fileInfo.Length / 1024.0:F1} KiB";

			using var reader = new StreamReader(filePath);
			Lines = File.ReadAllLines(filePath);
		} else {
			throw new FileNotFoundException($"找不到性能测试用的文本文件: {filePath}。请确保已将文件放置在Resource目录下。");
		}
	}

	public override string ToString() => FileName;
}