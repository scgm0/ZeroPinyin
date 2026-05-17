using System.Runtime.InteropServices;

namespace ZeroPinyin;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct PinyinRange(int Offset, int Count);