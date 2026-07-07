using System;

namespace TSLib.Audio;

/// <summary>
/// Активация передачи по кнопке (push-to-talk): пропускает звук только пока
/// кнопка зажата (<see cref="Talking"/>). При Enabled = false прозрачен.
/// </summary>
public sealed class PushToTalkPipe : ActivationPipe
{
	public bool Talking { get; set; }

	protected override bool ShouldPass(Span<byte> data, Meta? meta) => Talking;
}
