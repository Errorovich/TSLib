// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using TSLib.Audio;

namespace TSLib.Full;

/// <summary>
/// Byte-фрейминг голосовых пакетов (Voice/VoiceWhisper): сборка и отправка исходящих,
/// разбор мета-заголовка входящих. Потокобезопасен настолько же, насколько
/// <see cref="PacketHandler{TIn,TOut}.AddOutgoingPacket"/> (там lock) — можно звать с любого потока.
/// </summary>
internal static class VoicePacket
{
	/// <summary>Размер мета-заголовка входящего голосового пакета: [Seq:2][SenderId:2][Codec:1].</summary>
	public const int InHeaderSize = 5;

	/// <summary>Разбирает мета-заголовок входящего голосового пакета; полезная нагрузка начинается с <see cref="InHeaderSize"/>.</summary>
	public static Meta ParseInMeta(ReadOnlySpan<byte> packetData, bool whisper) => new Meta {
		In = new MetaIn {
			Sender = new ClientId(BinaryPrimitives.ReadUInt16BigEndian(packetData.Slice(2))),
			Seq = BinaryPrimitives.ReadUInt16BigEndian(packetData),
			Whisper = whisper,
		},
		Codec = (Codec)packetData[4],
	};

	public static void SendVoice(PacketHandler<S2C, C2S> packetHandler, in ReadOnlySpan<byte> data, Codec codec)
	{
		// [X,X,Y,DATA]
		// > X is a ushort in H2N order of an own audio packet counter
		//     it seems it can be the same as the packet counter so we will let the packethandler do it.
		// > Y is the codec byte (see Enum)
		Span<byte> tmpBuffer = stackalloc byte[data.Length + 3];
		tmpBuffer[2] = (byte)codec;
		data.CopyTo(tmpBuffer.Slice(3));

		packetHandler.AddOutgoingPacket(tmpBuffer, PacketType.Voice);
	}

	public static void SendWhisper(PacketHandler<S2C, C2S> packetHandler, in ReadOnlySpan<byte> data, Codec codec,
		IReadOnlyList<ChannelId> channelIds, IReadOnlyList<ClientId> clientIds)
	{
		// [X,X,Y,N,M,(U,U,U,U,U,U,U,U)*,(T,T)*,DATA]
		// > X is a ushort in H2N order of an own audio packet counter
		//     it seems it can be the same as the packet counter so we will let the packethandler do it.
		// > Y is the codec byte (see Enum)
		// > N is a byte, the count of ChannelIds to send to
		// > M is a byte, the count of ClientIds to send to
		// > U is a ulong in H2N order of each targeted channelId, (U...U) is repeated N times
		// > T is a ushort in H2N order of each targeted clientId, (T...T) is repeated M times
		int offset = 2 + 1 + 2 + channelIds.Count * 8 + clientIds.Count * 2;
		Span<byte> tmpBuffer = stackalloc byte[data.Length + offset];
		tmpBuffer[2] = (byte)codec;
		tmpBuffer[3] = (byte)channelIds.Count;
		tmpBuffer[4] = (byte)clientIds.Count;
		for (int i = 0; i < channelIds.Count; i++)
			BinaryPrimitives.WriteUInt64BigEndian(tmpBuffer.Slice(5 + (i * 8)), channelIds[i].Value);
		for (int i = 0; i < clientIds.Count; i++)
			BinaryPrimitives.WriteUInt16BigEndian(tmpBuffer.Slice(5 + channelIds.Count * 8 + (i * 2)), clientIds[i].Value);
		data.CopyTo(tmpBuffer.Slice(offset));

		packetHandler.AddOutgoingPacket(tmpBuffer, PacketType.VoiceWhisper);
	}

	public static void SendGroupWhisper(PacketHandler<S2C, C2S> packetHandler, in ReadOnlySpan<byte> data, Codec codec,
		GroupWhisperType type, GroupWhisperTarget target, ulong targetId = 0)
	{
		// [X,X,Y,N,M,U,U,U,U,U,U,U,U,DATA]
		// > X is a ushort in H2N order of an own audio packet counter
		//     it seems it can be the same as the packet counter so we will let the packethandler do it.
		// > Y is the codec byte (see Enum)
		// > N is a byte, specifying the GroupWhisperType
		// > M is a byte, specifying the GroupWhisperTarget
		// > U is a ulong in H2N order for the targeted channelId or groupId (0 if not applicable)
		Span<byte> tmpBuffer = stackalloc byte[data.Length + 13];
		tmpBuffer[2] = (byte)codec;
		tmpBuffer[3] = (byte)type;
		tmpBuffer[4] = (byte)target;
		BinaryPrimitives.WriteUInt64BigEndian(tmpBuffer.Slice(5), targetId);
		data.CopyTo(tmpBuffer.Slice(13));

		packetHandler.AddOutgoingPacket(tmpBuffer, PacketType.VoiceWhisper, PacketFlags.Newprotocol);
	}
}
