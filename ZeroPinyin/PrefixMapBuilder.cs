using System.Buffers;
using System.Runtime.CompilerServices;

namespace ZeroPinyin;

public static class PrefixMapBuilder {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private void GetFuzzyInitials(string? ini, bool enable, out string i0, out string? i1) {
		i0 = ini ?? "";
		i1 = null;
		if (!enable || ini == null) {
			return;
		}

		i1 = ini switch {
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
			_ => i1
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private void GetFuzzyFinals(string? fin, bool enable, out string f0, out string? f1) {
		f0 = fin ?? "";
		f1 = null;
		if (!enable || fin == null) {
			return;
		}

		f1 = fin switch {
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
			_ => f1
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private void AccumulateCount(int iniLen, int finLen, byte tone, ref int exactIntCount, ref int exactEndCount) {
		var len = iniLen + finLen;
		if (len == 0) {
			return;
		}

		exactIntCount += 2 + (iniLen > 0 ? 1 : 0) + (tone > 0 ? 1 : 0);
		exactEndCount += len + (tone > 0 ? 1 : 0);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static private void Populate(
		string ini,
		string fin,
		byte tone,
		ushort classId,
		ref int intPtr,
		ulong[] intKeys,
		ushort[] intIds,
		ref int endPtr,
		ulong[] endKeys,
		ushort[] endIds) {
		var iniLen = ini.Length;
		var finLen = fin.Length;
		var len = iniLen + finLen;
		if (len == 0) {
			return;
		}

		ulong encoded = 0;
		var shift = 0;
		for (var i = 0; i < iniLen; i++) {
			encoded |= (ulong)ini[i] << shift;
			shift += 8;
		}

		for (var i = 0; i < finLen; i++) {
			encoded |= (ulong)fin[i] << shift;
			shift += 8;
		}

		if (tone > 0) {
			encoded |= (ulong)('0' + tone) << shift;
		}

		intKeys[intPtr] = GetPrefix(1);
		intIds[intPtr++] = classId;
		if (iniLen > 0) {
			intKeys[intPtr] = GetPrefix(iniLen);
			intIds[intPtr++] = classId;
		}

		intKeys[intPtr] = GetPrefix(len);
		intIds[intPtr++] = classId;
		if (tone > 0) {
			intKeys[intPtr] = GetPrefix(len + 1);
			intIds[intPtr++] = classId;
		}

		for (var l = 1; l <= len; l++) {
			endKeys[endPtr] = GetPrefix(l);
			endIds[endPtr++] = classId;
		}

		if (tone > 0) {
			endKeys[endPtr] = GetPrefix(len + 1);
			endIds[endPtr++] = classId;
		}

		return;

		ulong GetPrefix(int l) => encoded & (l >= 8 ? ulong.MaxValue : (1UL << l * 8) - 1);
	}

	static private void Compress(
		ulong[] keys,
		ushort[] ids,
		int count,
		out ulong[] finalKeys,
		out PinyinRange[] finalRanges,
		out ushort[] finalValues) {
		if (count == 0) {
			finalKeys = [];
			finalRanges = [];
			finalValues = [];
			return;
		}

		Array.Sort(keys, ids, 0, count);

		var unique = 0;
		for (var i = 0; i < count; i++) {
			if (i == 0 || keys[i] != keys[i - 1] || ids[i] != ids[i - 1]) {
				keys[unique] = keys[i];
				ids[unique] = ids[i];
				unique++;
			}
		}

		var distinctKeys = 0;
		var lastKey = ulong.MaxValue;
		for (var i = 0; i < unique; i++) {
			if (keys[i] != lastKey) {
				distinctKeys++;
				lastKey = keys[i];
			}
		}

		finalKeys = new ulong[distinctKeys];
		finalRanges = new PinyinRange[distinctKeys];
		finalValues = new ushort[unique];

		lastKey = ulong.MaxValue;
		var startIdx = 0;
		var keyIdx = -1;
		for (var i = 0; i < unique; i++) {
			finalValues[i] = ids[i];
			if (keys[i] != lastKey) {
				if (keyIdx >= 0) {
					finalRanges[keyIdx] = new(startIdx, i - startIdx);
				}

				lastKey = keys[i];
				keyIdx++;
				finalKeys[keyIdx] = lastKey;
				startIdx = i;
			}
		}

		if (keyIdx >= 0) {
			finalRanges[keyIdx] = new(startIdx, unique - startIdx);
		}
	}

	public static PinyinPrefixData Build(HanziPinyinMap map, FuzzyConfig fuzzy) {
		var exactIntCount = 0;
		var exactEndCount = 0;
		var fuzzyIni = fuzzy.EnableFuzzyInitials;
		var fuzzyFin = fuzzy.EnableFuzzyFinals;

		for (ushort id = 256; id < map.ClassCount; id++) {
			var range = map.ClassRanges[id];
			var syllables = map.FlatSyllables.AsSpan(range.Offset, range.Count);
			for (var s = 0; s < syllables.Length; s++) {
				var py = syllables[s];
				GetFuzzyInitials(py.Initial, fuzzyIni, out var ini0, out var ini1);
				GetFuzzyFinals(py.Final, fuzzyFin, out var fin0, out var fin1);

				int ini0L = ini0.Length, fin0L = fin0.Length;
				int ini1L = ini1?.Length ?? 0, fin1L = fin1?.Length ?? 0;

				AccumulateCount(ini0L, fin0L, py.Tone, ref exactIntCount, ref exactEndCount);
				if (fin1 != null) {
					AccumulateCount(ini0L, fin1L, py.Tone, ref exactIntCount, ref exactEndCount);
				}

				if (ini1 != null) {
					AccumulateCount(ini1L, fin0L, py.Tone, ref exactIntCount, ref exactEndCount);
				}

				if (ini1 != null && fin1 != null) {
					AccumulateCount(ini1L, fin1L, py.Tone, ref exactIntCount, ref exactEndCount);
				}
			}
		}

		var intKeys = ArrayPool<ulong>.Shared.Rent(exactIntCount);
		var intIds = ArrayPool<ushort>.Shared.Rent(exactIntCount);
		var endKeys = ArrayPool<ulong>.Shared.Rent(exactEndCount);
		var endIds = ArrayPool<ushort>.Shared.Rent(exactEndCount);

		int intPtr = 0, endPtr = 0;

		for (ushort id = 256; id < map.ClassCount; id++) {
			var range = map.ClassRanges[id];
			var syllables = map.FlatSyllables.AsSpan(range.Offset, range.Count);
			for (var s = 0; s < syllables.Length; s++) {
				var py = syllables[s];
				GetFuzzyInitials(py.Initial, fuzzyIni, out var ini0, out var ini1);
				GetFuzzyFinals(py.Final, fuzzyFin, out var fin0, out var fin1);

				Populate(ini0, fin0, py.Tone, id, ref intPtr, intKeys, intIds, ref endPtr, endKeys, endIds);
				if (fin1 != null) {
					Populate(ini0, fin1, py.Tone, id, ref intPtr, intKeys, intIds, ref endPtr, endKeys, endIds);
				}

				if (ini1 != null) {
					Populate(ini1, fin0, py.Tone, id, ref intPtr, intKeys, intIds, ref endPtr, endKeys, endIds);
				}

				if (ini1 != null && fin1 != null) {
					Populate(ini1, fin1, py.Tone, id, ref intPtr, intKeys, intIds, ref endPtr, endKeys, endIds);
				}
			}
		}

		Compress(intKeys, intIds, exactIntCount, out var finalIntKeys, out var finalIntRanges, out var finalIntValues);
		Compress(endKeys, endIds, exactEndCount, out var finalEndKeys, out var finalEndRanges, out var finalEndValues);

		ArrayPool<ulong>.Shared.Return(intKeys);
		ArrayPool<ushort>.Shared.Return(intIds);
		ArrayPool<ulong>.Shared.Return(endKeys);
		ArrayPool<ushort>.Shared.Return(endIds);

		return new(finalIntKeys, finalIntRanges, finalIntValues, finalEndKeys, finalEndRanges, finalEndValues);
	}
}