using System;
using System.Collections.Generic;
using TSLib.Shared;

namespace TSLib.Audio.Pipes;

/// <summary>
/// Заглушка звука в конвейере. Поддерживает глобальное отключение (<see cref="Muted"/>)
/// и отключение отдельных клиентов по <see cref="ClientId"/> (мьют конкретного собеседника
/// на пути воспроизведения). Когда заглушено — данные дальше по конвейеру не идут.
/// </summary>
public sealed class MutePipe : IAudioPipe
{
	public bool Active => OutStream?.Active ?? false;
	public IAudioPassiveConsumer? OutStream { get; set; }

	public bool Muted { get; set; }

	private readonly HashSet<ClientId> mutedClients = new HashSet<ClientId>();
	private readonly object sync = new object();

	public void MuteClient(ClientId id, bool muted)
	{
		lock (sync)
		{
			if (muted)
				mutedClients.Add(id);
			else
				mutedClients.Remove(id);
		}
	}

	public bool IsClientMuted(ClientId id)
	{
		lock (sync)
			return mutedClients.Contains(id);
	}

	public void Write(Span<byte> data, Meta? meta)
	{
		if (OutStream is null)
			return;

		if (Muted)
			return;

		if (meta != null)
		{
			lock (sync)
			{
				if (mutedClients.Count != 0 && mutedClients.Contains(meta.In.Sender))
					return;
			}
		}

		OutStream.Write(data, meta);
	}
}
