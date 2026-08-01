using System.Runtime.InteropServices;

namespace ZeroPinyin;

/// <summary>扁平音节数组中的一个连续区间（偏移与数量）。</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PinyinRange(int Offset, int Count);