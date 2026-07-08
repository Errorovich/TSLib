// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using TSLib.Commands;
using TSLib.Messages;

namespace TSLib.Full;

/// <summary>
/// Шаги криптографического рукопожатия полного клиента:
/// initivexpand (TS3) либо initivexpand2 → clientek (TS3.1+/лицензионное), затем clientinit.
/// Сами команды отправляет <see cref="TsFullClient"/>, здесь — только криптография и сборка команд.
/// </summary>
internal static class FullClientHandshake
{
	/// <summary>Старое (TS3) рукопожатие: общий секрет из alpha/beta/omega.</summary>
	public static E<string> CryptoInit(ConnectionContext ctx, InitIvExpand notify)
		=> ctx.TsCrypt.CryptoInit(notify.Alpha, notify.Beta, notify.Omega);

	/// <summary>
	/// Новое (лицензионное) рукопожатие, фаза 1: генерирует временную ключевую пару,
	/// подписывает её identity-ключом и собирает команду clientek.
	/// Временный приватный ключ нужен затем в <see cref="CryptoInit2"/>.
	/// </summary>
	public static (TsCommand Command, byte[] TemporaryPrivateKey) BuildClientEk(ConnectionContext ctx, InitIvExpand2 notify)
	{
		var (publicKey, privateKey) = TsCrypt.GenerateTemporaryKey();

		var ekBase64 = Convert.ToBase64String(publicKey);
		var toSign = new byte[86];
		Array.Copy(publicKey, 0, toSign, 0, 32);
		var beta = Convert.FromBase64String(notify.Beta);
		Array.Copy(beta, 0, toSign, 32, 54);
		var sign = TsCrypt.Sign(ctx.ConnectionDataFull.Identity.PrivateKey, toSign);
		var proof = Convert.ToBase64String(sign);

		var command = new TsCommand("clientek") {
			{ "ek", ekBase64 },
			{ "proof", proof },
		};
		return (command, privateKey);
	}

	/// <summary>Новое рукопожатие, фаза 2 (после отправки clientek): общий секрет.</summary>
	public static E<string> CryptoInit2(ConnectionContext ctx, InitIvExpand2 notify, byte[] temporaryPrivateKey)
		=> ctx.TsCrypt.CryptoInit2(notify.License, notify.Omega, notify.Proof, notify.Beta, temporaryPrivateKey);

	/// <summary>Финальная команда clientinit с данными подключения (после успешного CryptoInit/CryptoInit2).</summary>
	public static TsCommand BuildClientInit(ConnectionDataFull cdf)
		=> new TsCommand("clientinit") {
			{ "client_nickname", cdf.Username },
			{ "client_version", cdf.VersionSign.Version },
			{ "client_platform", cdf.VersionSign.Platform },
			{ "client_input_hardware", true },
			{ "client_output_hardware", true },
			{ "client_default_channel", cdf.DefaultChannel },
			{ "client_default_channel_password", cdf.DefaultChannelPassword.HashedPassword }, // base64(sha1(pass))
			{ "client_server_password", cdf.ServerPassword.HashedPassword }, // base64(sha1(pass))
			{ "client_meta_data", string.Empty },
			{ "client_version_sign", cdf.VersionSign.Sign },
			{ "client_key_offset", cdf.Identity.ValidKeyOffset },
			{ "client_nickname_phonetic", string.Empty },
			{ "client_default_token", string.Empty },
			{ "hwid", cdf.Identity.ClientUid.ToString() },
		};
}
