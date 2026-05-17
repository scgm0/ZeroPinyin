using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeroPinyin;

public sealed class PinyinQuery {
	public string SearchText { get; }

	private readonly int _shift;
	private readonly ulong _acceptMask;
	private readonly ulong[] _flatTransitions;
	private readonly SearchValues<char>? _fastForwardChars;
	private readonly HanziPinyinMap _map;

	public PinyinQuery(ReadOnlySpan<char> search, HanziPinyinMap map, PinyinPrefixData prefixData) {
		if (search.Length >= 64) {
			throw new ArgumentException("搜索字符串过长（最多63个字符）");
		}

		SearchText = search.ToString();
		_map = map;
		var stateCount = search.Length + 1;
		_acceptMask = 1UL << search.Length;
		var classCount = map.ClassCount;

		var stride = (int)BitOperations.RoundUpToPowerOf2((uint)stateCount);
		_shift = BitOperations.TrailingZeroCount((uint)stride);

		_flatTransitions = new ulong[classCount * stride];

		var (intKeys, intRanges, intValues) = (prefixData.IntKeys, prefixData.IntRanges, prefixData.IntValues);
		var (endKeys, endRanges, endValues) = (prefixData.EndKeys, prefixData.EndRanges, prefixData.EndValues);

		for (var s = 0; s < search.Length; s++) {
			var c = search[s];
			var lowerClass = map.CharToClass[c];
			if (lowerClass != 0) {
				_flatTransitions[(lowerClass << _shift) + s] |= 1UL << s + 1;
			}

			if (IsPinyinChar(c)) {
				var maxLen = Math.Min(7, search.Length - s);
				ulong encoded = 0;

				for (var len = 1; len <= maxLen; len++) {
					ulong b = search[s + len - 1];
					if (b is >= 'A' and <= 'Z') {
						b |= 0x20;
					}

					encoded |= b << (len - 1) * 8;

					var isEnd = s + len == search.Length;
					var keys = isEnd ? endKeys : intKeys;
					var ranges = isEnd ? endRanges : intRanges;
					var values = isEnd ? endValues : intValues;

					var idx = Array.BinarySearch(keys, encoded);
					if (idx >= 0) {
						var range = ranges[idx];
						var mask = 1UL << s + len;
						var endIdx = range.Offset + range.Count;
						for (var i = range.Offset; i < endIdx; i++) {
							_flatTransitions[(values[i] << _shift) + s] |= mask;
						}
					}
				}
			}
		}

		var startCharsSet = new HashSet<char>();
		for (var i = 0; i < 65536; i++) {
			var cls = map.CharToClass[i];
			if (cls != 0 && _flatTransitions[cls << _shift] != 0) {
				startCharsSet.Add((char)i);
			}
		}

		if (startCharsSet.Count > 0) {
			Span<char> scSpan = stackalloc char[startCharsSet.Count];
			var idx = 0;
			foreach (var c in startCharsSet) scSpan[idx++] = c;
			_fastForwardChars = SearchValues.Create(scSpan);
		}
	}

	[SkipLocalsInit]
	public bool Contains(ReadOnlySpan<char> text) {
		if (text.IsEmpty) {
			return false;
		}

		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = 0; i < len; i++) {
			if (current == 0) {
				if (_fastForwardChars is null) {
					return false;
				}

				var pos = text[i..].IndexOfAny(_fastForwardChars);
				if (pos < 0) {
					return false;
				}

				i += pos;
			}

			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			var next = stateTransRef;

			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				next |= Unsafe.Add(ref stateTransRef, s);
			}

			if ((next & acceptMask) != 0) {
				return true;
			}

			current = next;
		}

		return false;
	}

	[SkipLocalsInit]
	public int CountMatches(ReadOnlySpan<char> text) {
		if (text.IsEmpty) {
			return 0;
		}

		var matchCount = 0;
		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = 0; i < len; i++) {
			if (current == 0) {
				if (_fastForwardChars is null) {
					return matchCount;
				}

				var pos = text[i..].IndexOfAny(_fastForwardChars);
				if (pos < 0) {
					return matchCount;
				}

				i += pos;
			}

			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			var next = stateTransRef;
			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				next |= Unsafe.Add(ref stateTransRef, s);
			}

			if ((next & acceptMask) != 0) {
				matchCount++;
				current = 0;
			} else {
				current = next;
			}
		}

		return matchCount;
	}

	[SkipLocalsInit]
	public bool EndsWith(ReadOnlySpan<char> text) {
		if (text.IsEmpty) {
			return false;
		}

		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = 0; i < len; i++) {
			if (current == 0) {
				if (_fastForwardChars is null) {
					return false;
				}

				var pos = text[i..].IndexOfAny(_fastForwardChars);
				if (pos < 0) {
					return false;
				}

				i += pos;
			}

			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			var next = stateTransRef;
			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				next |= Unsafe.Add(ref stateTransRef, s);
			}

			if (i == len - 1) {
				return (next & acceptMask) != 0;
			}

			current = next;
		}

		return false;
	}

	[SkipLocalsInit]
	public bool StartsWith(ReadOnlySpan<char> text) {
		if (text.IsEmpty) {
			return false;
		}

		ulong current = 1, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = 0; i < len; i++) {
			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			ulong next = 0;
			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				next |= Unsafe.Add(ref stateTransRef, s);
			}

			if ((next & acceptMask) != 0) {
				return true;
			}

			if (next == 0) {
				return false;
			}

			current = next;
		}

		return false;
	}

	[SkipLocalsInit]
	public bool IsMatch(ReadOnlySpan<char> text) {
		if (text.IsEmpty) {
			return false;
		}

		ulong current = 1, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = 0; i < len; i++) {
			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			ulong next = 0;
			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				next |= Unsafe.Add(ref stateTransRef, s);
			}

			if (next == 0) {
				return false;
			}

			current = next;
		}

		return (current & acceptMask) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private bool IsPinyinChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '5';
}