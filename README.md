# ZeroPinyin

ZeroPinyin 是一个为 **.NET 10+** 设计的高性能、零内存分配的即时中文拼音匹配引擎。

通过将非确定性有限状态自动机 (NFA) 压缩至 `ulong` 位运算，配合现代 C# 的底层内存控制特性（如 `Span<T>`、`ref locals`、`[InlineArray]`），能够在极低延迟下完成数百万行文本的拼音扫描，且搜索过程中不产生垃圾回收与内存分配。

## 📦 安装 (NuGet)

```bash
dotnet add package ZeroPinyin
```

## ✨ 主要特性

* **多模式匹配**：支持 `Contains`、`StartsWith`、`EndsWith`、`IsMatch` 和 `CountMatches`。
* **首字母缩写**：支持极简拼音首字母匹配（如搜索 `zg` 匹配 `中国`）。
* **多音字支持**：内置拼音数据来自[pinyin-data](https://github.com/mozillazg/pinyin-data)，支持常见多音字（如 `重庆` 匹配 `chongqing` 和 `zhongqing`）。
* **容错机制**：
  * **模糊音支持**：可选开启声母（zh/z, sh/s, n/l 等）和韵母（an/ang, in/ing 等）的模糊匹配。
  * **中英混拼**：支持中文、拼音、数字混搭搜索（如 `zhong国123` 匹配 `中国123`），且自带同音字容错。
  * **大小写不敏感**：忽略搜索串的大小写。
  * **音调匹配**：支持附加数字音调精确搜索（如 `yang2mao2` 匹配 `羊毛`），搜索串也支持 Unicode 声调符号（如 `yángmáo`）。
* **ü/v 兼容**：搜索串中的 `lü`、`lǜ` 与 `lv`、`lv4` 等价（内置拼音数据自动将 ü 归一为 v，如 `绿` 可用 `lv` 匹配）。
* **匹配定位**：`FindFirstIndex` 返回首个匹配的起始位置，`AllMatches` 零分配枚举所有不重叠匹配区间（起始/长度），便于高亮与跳转。
* ⚠️ **注意**：由于采用 `ulong` 寄存器作为底层状态机，单次搜索的字符串最大长度被硬性限制为 **63 个字符**（转换表按状态数线性扩张，超长查询会导致单查询数 MB 级内存占用，与 1024 项查询缓存架构冲突），这对于大部分的即时匹配场景已经足够；如需匹配超长文本，建议将搜索串分段后逐段匹配。

## 🛠 技术路线与底层优化

1. **NFA 状态机位运算压缩（Bit-Parallel NFA）**：将搜索关键词编译为扁平化的二维状态掩码矩阵，在单个 `ulong` 内动态计算子集。
2. **零分配搜索 (Zero Allocation)**：
   * 搜索方法全量使用 `ReadOnlySpan<char>`。
   * 循环体内部使用 `ref locals`、`MemoryMarshal` 和 `Unsafe.Add` 规避所有的数组边界检查。
   * 利用 `allows ref struct` 泛型约束，通过 `AlternateLookup` 实现无装箱的 `ReadOnlySpan<char>` 字典缓存查询。
3. **SIMD 硬件加速前置过滤**：利用 .NET 的 `SearchValues<char>`（底层基于向量化指令如 AVX2），在匹配开始前极速跳过不相关的文本，加速在长文本中寻找稀疏匹配项的过程。
4. **极致的内存结构布局**：
   * 在初始化解析拼音字典时，使用 `[InlineArray]` 结合 `[StructLayout(Pack = 1)]`，将多音字状态原位压缩至最小结构体。
   * 利用基于换行符计数的算法提前分配 `Dictionary` 与 `List` 的容量，尽量消除内部数组扩容带来的堆碎片（Gen1/Gen2 回收）。

## 🚀 快速起步

### 1. 基础用法

```csharp
using ZeroPinyin;

// 自带单例，内部已做好状态机的缓存
var matcher = PinyinMatcher.Default;

// 基础匹配
bool result1 = matcher.Contains("羊毛", "yangmao"); // True
bool result2 = matcher.StartsWith("羊毛", "yang");     // True
bool result3 = matcher.EndsWith("薅羊毛", "mao");        // True

// 首字母与多音字
bool result4 = matcher.Contains("中华人民共和国", "zhrmghg"); // True
bool result5 = matcher.Contains("长江", "zhangjiang");      // True (多音字:长)

// 混合匹配与容错
bool result6 = matcher.Contains("中国", "zhong国"); // True
bool result7 = matcher.Contains("知识", "zisi");    // True (默认开启模糊音)

// 声调符号与 ü 输入
bool result8 = matcher.Contains("羊毛", "yángmáo"); // True (声调符号自动规范化)
bool result9 = matcher.Contains("绿", "lü");        // True (ü 自动归一为 v)

// 匹配定位（高亮/跳转）
int index = matcher.FindFirstIndex("一只羊毛", "yangmao"); // 2
var matches = matcher.AllMatches("羊毛羊毛", "yangmao");   // [0,2]、[2,2]
while (matches.MoveNext()) {
    var range = matches.Current; // Start / Length / End
}
```

### 2. 自定义模糊音配置

如果你需要严谨的匹配（例如关闭平翘舌模糊音）：

```csharp
var fuzzyOff = new FuzzyConfig { 
    EnableFuzzyInitials = false, 
    EnableFuzzyFinals = false 
};
var strictMatcher = new PinyinMatcher(HanziPinyinMap.Default, fuzzyOff);

strictMatcher.Contains("知识", "zhishi"); // True
strictMatcher.Contains("知识", "zisi");   // False
```

### 3. 自定义拼音数据

如果内置拼音数据不满足需求（如需要添加特殊生僻字或自定义发音），可以轻松注入自己的文本：

```csharp
var myData = "U+4E2D: zhong1,zhong4\nU+56FD: guo2"; // 也可使用原始的拼音数据，比如"U+4E2D: zhōng,zhòng"
var customMap = new HanziPinyinMap(myData);
var customMatcher = new PinyinMatcher(customMap);
```

## 📊 性能基准测试

以下测试运行于 **GitHub Actions CI**（.NET 10.0 SDK，X64 RyuJIT），对比了在两组来自[PinIn](https://github.com/Towdium/PinIn)的数据集下执行拼音匹配的性能。

* **large.txt**：13.5 MiB，1,000,000 行文本
* **small.txt**：866.0 KiB，37,450 行文本

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  Job-XPUURG : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=5  
```

| Method              | Query   | FileParam | Size      | Lines     | Mean        | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|-------------------- |-------- |---------- |----------:|----------:|------------:|----------:|----------:|---------:|---------:|---------:|----------:|
| **Init**            | **yangmao** | **large.txt** | **13.5 MiB** | **1,000,000** | **21,247.9 μs** | **141.56 μs** |  **93.64 μs** |        **-** |        **-** |        **-** | **1666456 B** |
| Contains            | yangmao | large.txt |  13.5 MiB | 1,000,000 | 22,919.5 μs | 152.57 μs | 100.91 μs |        - |        - |        - |         - |
| CountMatches        | yangmao | large.txt |  13.5 MiB | 1,000,000 | 23,320.3 μs |  68.23 μs |  45.13 μs |        - |        - |        - |         - |
| StartsWith          | yangmao | large.txt |  13.5 MiB | 1,000,000 |  8,809.5 μs |  20.56 μs |  13.60 μs |        - |        - |        - |         - |
| EndsWith            | yangmao | large.txt |  13.5 MiB | 1,000,000 | 22,710.1 μs |  52.60 μs |  31.30 μs |        - |        - |        - |         - |
| IsMatch             | yangmao | large.txt |  13.5 MiB | 1,000,000 |  8,747.1 μs |  54.75 μs |  32.58 μs |        - |        - |        - |         - |
| ColdCompile         | yangmao | large.txt |  13.5 MiB | 1,000,000 |    324.6 μs |  15.60 μs |  10.32 μs |  11.7188 |   6.8359 |        - | 1104320 B |
| MultiThreadCacheHit | yangmao | large.txt |  13.5 MiB | 1,000,000 | 16,689.4 μs | 190.66 μs | 126.11 μs |        - |        - |        - |    3904 B |
| **Init**            | **yangmao** | **small.txt** | **866.0 KiB** |    **37,450** | **21,235.5 μs** |  **96.91 μs** |  **57.67 μs** | **156.2500** | **156.2500** | **156.2500** | **1667886 B** |
| Contains            | yangmao | small.txt | 866.0 KiB |    37,450 |  1,138.7 μs |   2.19 μs |   1.45 μs |        - |        - |        - |         - |
| CountMatches        | yangmao | small.txt | 866.0 KiB |    37,450 |  1,136.4 μs |   1.25 μs |   0.74 μs |        - |        - |        - |         - |
| StartsWith          | yangmao | small.txt | 866.0 KiB |    37,450 |    277.9 μs |   1.55 μs |   0.92 μs |        - |        - |        - |         - |
| EndsWith            | yangmao | small.txt | 866.0 KiB |    37,450 |    677.5 μs |   2.51 μs |   1.49 μs |        - |        - |        - |         - |
| IsMatch             | yangmao | small.txt | 866.0 KiB |    37,450 |    274.2 μs |   1.67 μs |   0.99 μs |        - |        - |        - |         - |
| ColdCompile         | yangmao | small.txt | 866.0 KiB |    37,450 |    331.4 μs |  16.94 μs |  10.08 μs |  12.6953 |   7.8125 |   0.9766 | 1104329 B |
| MultiThreadCacheHit | yangmao | small.txt | 866.0 KiB |    37,450 | 17,044.2 μs | 236.34 μs | 156.33 μs |        - |        - |        - |    3904 B |

<details>
<summary><b>点击查看数据字段说明</b></summary>

```text
  Query     : 测试使用的拼音
  FileParam : 测试使用的文本文件
  Size      : 测试文本的大小
  Lines     : 测试文本的行数，每行单独调用匹配方法
  Mean      : 所有测量结果的算术平均值
  Error     : 99.9%置信区间的一半
  StdDev    : 所有测量结果的标准差
  Gen0      : 每1000次操作中第0代垃圾回收次数
  Gen1      : 每1000次操作中第1代垃圾回收次数
  Gen2      : 每1000次操作中第2代垃圾回收次数
  Allocated : 单次操作分配的内存（仅托管内存，包含所有分配，1KB = 1024B）
  1 μs      : 1微秒（0.000001秒）
  ColdCompile      : 缓存未命中时编译一个全新查询（模拟输入法每按键的新词条）
  MultiThreadCacheHit : 8 线程并发各轮换 64 个已缓存查询，总 800,000 次命中
```
</details>

### 测试结论说明：
* **`Init`（初始化）**：启动阶段构建`HanziPinyinMap`与`PinyinMatcher`耗时约 21ms，一次性分配约 1.59 MiB 内存，此后字典数据常驻内存供复用。
* **`ColdCompile`（冷编译）**：缓存未命中时编译新查询仅需约 **0.33ms**，单字符前缀直接映射表（O(1) 查表替代二分查找）加速编译路径。
* **`MultiThreadCacheHit`（多线程缓存命中）**：8 线程并发共享同一匹配器（总 800,000 次命中）约 **17ms**，两参数行差异仅 2.1%（原生线程测量，稳定可靠）；相比旧版单锁方案提升约 **30%**（无锁缓存读取 + 线程局部快速路径消除锁竞争；本地笔记本实测提升可达 5-13 倍，取决于锁竞争程度）。
* **搜索过程（Allocated = `-`）**：在搜索方法执行时，**堆内存分配为 0 Byte**，这意味着哪怕以极高频率并发搜索，也不会给垃圾回收器（GC）带来任何压力。
* **高吞吐量**：在 100 万行（13.5 MiB）文本的 `Contains` 遍历搜索中，耗时约 23 毫秒（即每秒可扫描处理近 4400 万行文本）。

> 注：CI runner 为共享租户（AMD EPYC 7763/9V74），不同运行的 CPU 型号与频率存在差异（2.45-2.87GHz）；经对照验证，性能优化后的热循环与旧版持平或更快，多线程缓存命中快约 30%，无性能退化。

## 📦 依赖与兼容性

* 运行时：**.NET 10.0+**。
* 语言版本：**C# 14+**。
* 无第三方依赖。
* 内置[pinyin-data](https://github.com/mozillazg/pinyin-data)的拼音数据文件（约 4.4 万汉字），也可使用自定义拼音数据构建`HanziPinyinMap`。

## 📄 开源协议

本项目采用 [MIT License](LICENSE) 开源协议。
