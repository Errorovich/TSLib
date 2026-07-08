using System;

namespace TSLib.Audio.Pipes;

/// <summary>
/// Активация передачи по голосу (VAD): пропускает звук пока громкость выше порога,
/// плюс настраиваемая задержка деактивации после падения громкости (чтобы не обрезать
/// концы слов). <see cref="IsOpen"/> отражает, идёт ли сейчас передача.
/// При Enabled = false прозрачен.
/// </summary>
public sealed class VoiceActivationPipe : ActivationPipe
{
	/// <summary>Порог срабатывания 0..1 (сопоставим с <see cref="AudioTools.Peak"/>).</summary>
	public float Threshold { get; set; } = 0.05f;

	/// <summary>Задержка деактивации в мс: сколько держать «открытым» после падения громкости ниже порога.</summary>
	public int DeactivationDelayMs { get; set; } = 300;

	public bool IsOpen { get; private set; }
	public event Action<bool>? OnOpenChanged;

	private long openUntil;

	protected override void OnEnabledChanged(bool enabled)
	{
		if (!enabled)
			SetOpen(false); // прозрачный режим — состояние «открыто» не отслеживаем
	}

	protected override bool ShouldPass(Span<byte> data, Meta? meta)
	{
		long now = Environment.TickCount64;
		if (AudioTools.Peak(data) >= Threshold)
			openUntil = now + DeactivationDelayMs;

		bool open = now <= openUntil;
		SetOpen(open);
		return open;
	}

	private void SetOpen(bool value)
	{
		if (IsOpen == value)
			return;
		IsOpen = value;
		OnOpenChanged?.Invoke(value);
	}
}
