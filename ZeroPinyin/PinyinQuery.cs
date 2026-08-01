using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeroPinyin;

public sealed class PinyinQuery {
	public string SearchText { get; }

	private readonly int _searchLength;
	private readonly int _shift;
	private readonly ulong _acceptMask;
	private readonly ulong[] _flatTransitions;
	private readonly SearchValues<char>? _fastForwardChars;
	private readonly HanziPinyinMap _map;

	private readonly ulong _exactMatchMask;
	private readonly char[]? _exactChars;

	public PinyinQuery(ReadOnlySpan<char> search, HanziPinyinMap map, PinyinPrefixData prefixData, FuzzyConfig config) {
		if (search.Length >= 64) {
			throw new ArgumentException("搜索字符串过长（最多63个字符）");
		}

		SearchText = search.ToString();
		var searchLength = _searchLength = search.Length;
		_map = map;
		var stateCount = searchLength + 1;
		_acceptMask = 1UL << searchLength;
		var classCount = map.ClassCount;

		var stride = (int)BitOperations.RoundUpToPowerOf2((uint)stateCount);
		_shift = BitOperations.TrailingZeroCount((uint)stride);

		_flatTransitions = new ulong[classCount * stride];

		var exactHanzi = config.ExactMatchForHanzi;
		ulong exactMatchMask = 0;
		char[]? exactChars = null;

		var (intKeys, intRanges, intValues, singleCharRanges) = (prefixData.IntKeys, prefixData.IntRanges, prefixData.IntValues, prefixData.SingleCharRanges);
		var (endKeys, endRanges, endValues) = (prefixData.EndKeys, prefixData.EndRanges, prefixData.EndValues);

		for (var s = 0; s < searchLength; s++) {
			var c = search[s];
			var lowerClass = map.CharToClass[c];
			var isPinyin = IsPinyinChar(c);

			if (exactHanzi && !isPinyin && c >= 256) {
				exactMatchMask |= 1UL << (s + 1);
				exactChars ??= new char[64];
				exactChars[s + 1] = c;

				if (lowerClass != 0) {
					_flatTransitions[(lowerClass << _shift) + s] |= 1UL << (s + 1);
				}
			} else {
				if (lowerClass != 0) {
					_flatTransitions[(lowerClass << _shift) + s] |= 1UL << (s + 1);
				}

				if (isPinyin) {
					var maxLen = Math.Min(7, searchLength - s);
					ulong encoded = 0;

					for (var len = 1; len <= maxLen; len++) {
						ulong b = search[s + len - 1];
						if (b is >= 'A' and <= 'Z') {
							b |= 0x20;
						}

						encoded |= b << (len - 1) * 8;

						var isEnd = s + len == searchLength;
						var keys = isEnd ? endKeys : intKeys;
						var ranges = isEnd ? endRanges : intRanges;
						var values = isEnd ? endValues : intValues;

						var idx = len == 1 ? singleCharRanges[(byte)encoded] : Array.BinarySearch(keys, encoded);
						if (idx >= 0) {
							var range = ranges[idx];
							var mask = 1UL << (s + len);
							var endIdx = range.Offset + range.Count;
							for (var i = range.Offset; i < endIdx; i++) {
								_flatTransitions[(values[i] << _shift) + s] |= mask;
							}
						}
					}
				} else if (c >= 256 && lowerClass >= 256) {
					var rangeC = map.ClassRanges[lowerClass];
					var sylsC = map.FlatSyllables.AsSpan(rangeC.Offset, rangeC.Count);

					for (var cls = 256; cls < classCount; cls++) {
						var rangeOther = map.ClassRanges[cls];
						var sylsOther = map.FlatSyllables.AsSpan(rangeOther.Offset, rangeOther.Count);

						var match = false;
						for (var i = 0; i < sylsC.Length && !match; i++) {
							var searchSyl = sylsC[i];
							for (var j = 0; j < sylsOther.Length && !match; j++) {
								var textSyl = sylsOther[j];
								if (IsFuzzyEqual(searchSyl, textSyl, config)) {
									match = true;
								}
							}
						}

						if (match) {
							_flatTransitions[(cls << _shift) + s] |= 1UL << (s + 1);
						}
					}
				}
			}
		}

		_exactMatchMask = exactMatchMask;
		_exactChars = exactChars;

		var startCharsSet = new HashSet<char>();
		for (var i = 0; i < 65536; i++) {
			var cls = map.CharToClass[i];
			if (cls != 0) {
				var nextFrom0 = _flatTransitions[cls << _shift];
				if (nextFrom0 != 0) {
					if (exactMatchMask != 0) {
						var exactCheck = nextFrom0 & exactMatchMask;
						if (exactCheck != 0) {
							var bits = exactCheck;
							while (bits != 0) {
								var s = BitOperations.TrailingZeroCount(bits);
								bits &= bits - 1;
								if (i != exactChars![s]) {
									nextFrom0 &= ~(1UL << s);
								}
							}
						}
					}

					if (nextFrom0 != 0) {
						startCharsSet.Add((char)i);
					}
				}
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
		if (text.IsEmpty || _fastForwardChars is null) {
			return false;
		}

		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = 0; i < len; i++) {
			if (current == 0) {
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

			if ((next & _exactMatchMask) != 0) {
				var exactCheck = next & _exactMatchMask;
				var textChar = Unsafe.Add(ref textRef, i);
				ref var exactCharsRef = ref MemoryMarshal.GetArrayDataReference(_exactChars!);
				while (exactCheck != 0) {
					var s = BitOperations.TrailingZeroCount(exactCheck);
					exactCheck &= exactCheck - 1;
					if (textChar != Unsafe.Add(ref exactCharsRef, s)) {
						next &= ~(1UL << s);
					}
				}
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
		if (text.IsEmpty || _fastForwardChars is null) {
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

			if ((next & _exactMatchMask) != 0) {
				var exactCheck = next & _exactMatchMask;
				var textChar = Unsafe.Add(ref textRef, i);
				ref var exactCharsRef = ref MemoryMarshal.GetArrayDataReference(_exactChars!);
				while (exactCheck != 0) {
					var s = BitOperations.TrailingZeroCount(exactCheck);
					exactCheck &= exactCheck - 1;
					if (textChar != Unsafe.Add(ref exactCharsRef, s)) {
						next &= ~(1UL << s);
					}
				}
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
		if (text.IsEmpty || _fastForwardChars is null) {
			return false;
		}

		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length, startIdx = len - _searchLength;
		if (startIdx < 0) {
			startIdx = 0;
		}

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		for (var i = startIdx; i < len; i++) {
			if (current == 0) {
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

			if ((next & _exactMatchMask) != 0) {
				var exactCheck = next & _exactMatchMask;
				var textChar = Unsafe.Add(ref textRef, i);
				ref var exactCharsRef = ref MemoryMarshal.GetArrayDataReference(_exactChars!);
				while (exactCheck != 0) {
					var s = BitOperations.TrailingZeroCount(exactCheck);
					exactCheck &= exactCheck - 1;
					if (textChar != Unsafe.Add(ref exactCharsRef, s)) {
						next &= ~(1UL << s);
					}
				}
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

			if ((next & _exactMatchMask) != 0) {
				var exactCheck = next & _exactMatchMask;
				var textChar = Unsafe.Add(ref textRef, i);
				ref var exactCharsRef = ref MemoryMarshal.GetArrayDataReference(_exactChars!);
				while (exactCheck != 0) {
					var s = BitOperations.TrailingZeroCount(exactCheck);
					exactCheck &= exactCheck - 1;
					if (textChar != Unsafe.Add(ref exactCharsRef, s)) {
						next &= ~(1UL << s);
					}
				}
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

			if ((next & _exactMatchMask) != 0) {
				var exactCheck = next & _exactMatchMask;
				var textChar = Unsafe.Add(ref textRef, i);
				ref var exactCharsRef = ref MemoryMarshal.GetArrayDataReference(_exactChars!);
				while (exactCheck != 0) {
					var s = BitOperations.TrailingZeroCount(exactCheck);
					exactCheck &= exactCheck - 1;
					if (textChar != Unsafe.Add(ref exactCharsRef, s)) {
						next &= ~(1UL << s);
					}
				}
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private bool IsFuzzyEqual(PinyinSyllable searchSyl, PinyinSyllable textSyl, FuzzyConfig config) {
		var iniMatch = searchSyl.InitialIdx == textSyl.InitialIdx;
		if (!iniMatch && config.EnableFuzzyInitials) {
			var iniText = textSyl.Initial;
			var i1 = iniText switch {
				"zh" => "z",
				"z" => "zh",
				"ch" => "c",
				"c" => "ch",
				"sh" => "s",
				"s" => "sh",
				"n" => "l",
				"l" => "n",
				"f" => "h",
				"h" => "f",
				"r" => "l",
				_ => null
			};
			iniMatch = i1 != null && i1 == searchSyl.Initial;
		}

		if (!iniMatch) {
			return false;
		}

		var finMatch = searchSyl.FinalIdx == textSyl.FinalIdx;
		if (!finMatch && config.EnableFuzzyFinals) {
			var finText = textSyl.Final;
			var f1 = finText switch {
				"an" => "ang",
				"ang" => "an",
				"en" => "eng",
				"eng" => "en",
				"in" => "ing",
				"ing" => "in",
				"ian" => "iang",
				"iang" => "ian",
				"uan" => "uang",
				"uang" => "uan",
				_ => null
			};
			finMatch = f1 != null && f1 == searchSyl.Final;
		}

		return finMatch;
	}
}