using System;

namespace TSLib.Audio;

/// <summary>
/// Базовый пайп-«воротца» с переключателем режима. Когда <see cref="Enabled"/> = false —
/// полностью прозрачен (режим не выбран). Когда true — пропускает кадр только если наследник
/// разрешил его в <see cref="ShouldPass"/>. Удобно держать несколько активационных пайпов
/// в одной цепочке и включать ровно один.
/// </summary>
public abstract class ActivationPipe : IAudioPipe
{
	public bool Active => OutStream?.Active ?? false;
	public IAudioPassiveConsumer? OutStream { get; set; }

	private bool enabled;
	public bool Enabled
	{
		get => enabled;
		set
		{
			if (enabled == value)
				return;
			enabled = value;
			OnEnabledChanged(value);
		}
	}

	/// <summary>Уведомление наследнику о смене режима (например, сбросить внутреннее состояние).</summary>
	protected virtual void OnEnabledChanged(bool enabled) { }

	/// <summary>Пропускать ли текущий кадр. Вызывается только при <see cref="Enabled"/> = true.</summary>
	protected abstract bool ShouldPass(Span<byte> data, Meta? meta);

	public void Write(Span<byte> data, Meta? meta)
	{
		if (OutStream is null)
			return;

		if (enabled && !ShouldPass(data, meta))
			return;

		OutStream.Write(data, meta);
	}
}
