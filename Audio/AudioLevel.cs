using System;
using System.Runtime.InteropServices;

namespace TSLib.Audio;

/// <summary>
/// Измерение громкости PCM16-кадра. Один и тот же расчёт используют активация по голосу
/// (<see cref="VoiceActivationPipe"/>) и индикаторы уровня в UI — чтобы порог совпадал
/// с тем, что видно на полоске.
/// </summary>
public static class AudioLevel
{
	/// <summary>Пиковая амплитуда кадра, нормированная в диапазон 0..1.</summary>
	public static float Peak(ReadOnlySpan<byte> pcm16)
	{
		var samples = MemoryMarshal.Cast<byte, short>(pcm16);
		int peak = 0;
		for (int i = 0; i < samples.Length; i++)
		{
			int v = samples[i];
			v = v < 0 ? -v : v; // -32768 при инверсии останется 32767 после клампа ниже
			if (v > peak)
				peak = v;
		}
		return Math.Min(peak / (float)short.MaxValue, 1f);
	}
}
