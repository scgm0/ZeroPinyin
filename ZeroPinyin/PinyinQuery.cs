using System.Buffers;
using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeroPinyin;

/// <summary>
/// 编译后的拼音查询（不可变）：持有 NFA 转换矩阵并提供多模式匹配，全部方法零内存分配。
/// </summary>
public sealed class PinyinQuery {
	/// <summary>搜索串原文（规范化后）。</summary>
	public string SearchText { get; }

	private readonly int _searchLength;
	private readonly int _shift;
	private readonly ulong _acceptMask;
	private readonly ulong[] _flatTransitions;
	private readonly SearchValues<char>? _fastForwardChars;
	private readonly HanziPinyinMap _map;

	private readonly ulong _exactMatchMask;
	private readonly char[]? _exactChars;

	/// <summary>
	/// 编译搜索串（通常通过 <see cref="PinyinMatcher.Compile"/> 获得，带缓存）。
	/// </summary>
	/// <param name="search">拼音搜索串（不超过 63 个字符）。</param>
	/// <param name="map">汉字拼音映射表。</param>
	/// <param name="prefixData">拼音前缀索引。</param>
	/// <param name="config">模糊配置。</param>
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

	/// <summary>判断文本中是否包含搜索串的拼音匹配。</summary>
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

	/// <summary>统计文本中不重叠匹配的数量。</summary>
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

	/// <summary>
	/// 返回文本中第一个匹配的起始索引，无匹配时返回 -1。
	/// 匹配区间为不重叠语义（与 <see cref="CountMatches"/> 一致）。
	/// </summary>
	/// <param name="text">待搜索文本。</param>
	/// <returns>匹配起始索引，或 -1。</returns>
	[SkipLocalsInit]
	public int FindFirstIndex(ReadOnlySpan<char> text) {
		if (text.IsEmpty || _fastForwardChars is null) {
			return -1;
		}

		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		Span<int> startPos = stackalloc int[64];
		startPos.Fill(-1);

		for (var i = 0; i < len; i++) {
			if (current == 0) {
				var pos = text[i..].IndexOfAny(_fastForwardChars);
				if (pos < 0) {
					return -1;
				}

				i += pos;
			}

			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			var next = stateTransRef;

			var initBits = next;
			while (initBits != 0) {
				var t = BitOperations.TrailingZeroCount(initBits);
				initBits &= initBits - 1;
				if (startPos[t] == -1 || i < startPos[t]) {
					startPos[t] = i;
				}
			}

			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				var rowBits = Unsafe.Add(ref stateTransRef, s);
				var newBits = rowBits & ~next;
				while (newBits != 0) {
					var t = BitOperations.TrailingZeroCount(newBits);
					newBits &= newBits - 1;
					if (startPos[t] == -1 || startPos[s] < startPos[t]) {
						startPos[t] = startPos[s];
					}
				}

				next |= rowBits;
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
				return startPos[_searchLength];
			}

			current = next;
		}

		return -1;
	}

	/// <summary>
	/// 返回文本中第一个匹配的完整区间（Start..End，End 不含），无匹配时返回 null。
	/// 可用 <c>text[result.Value]</c> 零分配获取匹配切片（先判空）。
	/// </summary>
	/// <param name="text">待搜索文本。</param>
	/// <returns>匹配区间；无匹配时为 null。</returns>
	[SkipLocalsInit]
	public Range? FindFirstMatch(ReadOnlySpan<char> text) {
		if (text.IsEmpty || _fastForwardChars is null) {
			return null;
		}

		ulong current = 0, acceptMask = _acceptMask;
		int shift = _shift, len = text.Length;

		ref var textRef = ref MemoryMarshal.GetReference(text);
		ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(_map.CharToClass);
		ref var transRef = ref MemoryMarshal.GetArrayDataReference(_flatTransitions);

		Span<int> startPos = stackalloc int[64];
		startPos.Fill(-1);

		for (var i = 0; i < len; i++) {
			if (current == 0) {
				var pos = text[i..].IndexOfAny(_fastForwardChars);
				if (pos < 0) {
					return null;
				}

				i += pos;
			}

			var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
			ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
			var next = stateTransRef;

			var initBits = next;
			while (initBits != 0) {
				var t = BitOperations.TrailingZeroCount(initBits);
				initBits &= initBits - 1;
				if (startPos[t] == -1 || i < startPos[t]) {
					startPos[t] = i;
				}
			}

			var bits = current & ~acceptMask;
			while (bits != 0) {
				var s = BitOperations.TrailingZeroCount(bits);
				bits &= bits - 1;
				var rowBits = Unsafe.Add(ref stateTransRef, s);
				var newBits = rowBits & ~next;
				while (newBits != 0) {
					var t = BitOperations.TrailingZeroCount(newBits);
					newBits &= newBits - 1;
					if (startPos[t] == -1 || startPos[s] < startPos[t]) {
						startPos[t] = startPos[s];
					}
				}

				next |= rowBits;
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
				return new(startPos[_searchLength], i + 1);
			}

			current = next;
		}

		return null;
	}

	/// <summary>
	/// 枚举文本中所有不重叠匹配的区间（与 <see cref="CountMatches"/> 计数语义一致），零分配。
	/// </summary>
	/// <param name="text">待搜索文本。</param>
	/// <returns>匹配区间枚举器。</returns>
	public MatchEnumerator AllMatches(ReadOnlySpan<char> text) => new(this, text);

	/// <summary>
	/// 匹配区间枚举器，用于遍历 <see cref="PinyinQuery.AllMatches"/> 的结果。
	/// </summary>
	public ref struct MatchEnumerator {
		private readonly PinyinQuery _q;
		private readonly ReadOnlySpan<char> _text;
		private int _index;
		private Range _current;

		internal MatchEnumerator(PinyinQuery q, ReadOnlySpan<char> text) {
			_q = q;
			_text = text;
			_index = 0;
			_current = default;
		}

		/// <summary>当前匹配区间（Start..End，End 不含；可用 text[Current] 零分配切片）。</summary>
		public readonly Range Current => _current;

		/// <summary>
		/// 返回自身，使 <see cref="MatchEnumerator"/> 支持 <c>foreach</c> 枚举（鸭子类型模式，零分配）。
		/// </summary>
		public MatchEnumerator GetEnumerator() => this;

		/// <summary>
		/// 前进到下一个匹配区间。
		/// </summary>
		/// <returns>存在下一个匹配时为 true。</returns>
		[SkipLocalsInit]
		public bool MoveNext() {
			var q = _q;
			var text = _text;
			if (text.IsEmpty || q._fastForwardChars is null) {
				return false;
			}

			ulong current = 0, acceptMask = q._acceptMask;
			int shift = q._shift, len = text.Length;
			var start = _index;

			ref var textRef = ref MemoryMarshal.GetReference(text);
			ref var charToClassRef = ref MemoryMarshal.GetArrayDataReference(q._map.CharToClass);
			ref var transRef = ref MemoryMarshal.GetArrayDataReference(q._flatTransitions);

			Span<int> startPos = stackalloc int[64];
			startPos.Fill(-1);

			for (var i = start; i < len; i++) {
				if (current == 0) {
					var pos = text[i..].IndexOfAny(q._fastForwardChars);
					if (pos < 0) {
						return false;
					}

					i += pos;
				}

				var charCls = Unsafe.Add(ref charToClassRef, Unsafe.Add(ref textRef, i));
				ref var stateTransRef = ref Unsafe.Add(ref transRef, (nint)charCls << shift);
				var next = stateTransRef;

				var initBits = next;
				while (initBits != 0) {
					var t = BitOperations.TrailingZeroCount(initBits);
					initBits &= initBits - 1;
					if (startPos[t] == -1 || i < startPos[t]) {
						startPos[t] = i;
					}
				}

				var bits = current & ~acceptMask;
				while (bits != 0) {
					var s = BitOperations.TrailingZeroCount(bits);
					bits &= bits - 1;
					var rowBits = Unsafe.Add(ref stateTransRef, s);
					var newBits = rowBits & ~next;
					while (newBits != 0) {
						var t = BitOperations.TrailingZeroCount(newBits);
						newBits &= newBits - 1;
						if (startPos[t] == -1 || startPos[s] < startPos[t]) {
							startPos[t] = startPos[s];
						}
					}

					next |= rowBits;
				}

				if ((next & q._exactMatchMask) != 0) {
					var exactCheck = next & q._exactMatchMask;
					var textChar = Unsafe.Add(ref textRef, i);
					ref var exactCharsRef = ref MemoryMarshal.GetArrayDataReference(q._exactChars!);
					while (exactCheck != 0) {
						var s = BitOperations.TrailingZeroCount(exactCheck);
						exactCheck &= exactCheck - 1;
						if (textChar != Unsafe.Add(ref exactCharsRef, s)) {
							next &= ~(1UL << s);
						}
					}
				}

				if ((next & acceptMask) != 0) {
					var mStart = startPos[q._searchLength];
					_current = new(mStart, i + 1);
					_index = i + 1;
					return true;
				}

				current = next;
			}

			return false;
		}
	}

	/// <summary>判断文本是否以搜索串的拼音匹配结束。</summary>
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

	/// <summary>判断文本是否以搜索串的拼音匹配开始。</summary>
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

	/// <summary>判断整个文本是否与搜索串完全匹配。</summary>
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