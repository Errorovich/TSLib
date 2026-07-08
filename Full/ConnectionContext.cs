// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Threading.Tasks;
using TSLib.Messages;
using TSLib.Crypto;
using TSLib.Full.Transport;
using TSLib.Shared;

namespace TSLib.Full;

/// <summary>
/// Всё состояние одного подключения. Живёт от Connect до Disconnect;
/// на каждое новое подключение создаётся новый контекст.
/// </summary>
internal class ConnectionContext
{
	// Счётчик return_code для корреляции ответов сервера с запросами; свой на каждое подключение.
	private uint returnCode;

	public Reason? ExitReason { get; set; }
	public TsCrypt TsCrypt { get; }
	public PacketHandler<S2C, C2S> PacketHandler { get; }
	public ConnectionDataFull ConnectionDataFull { get; }

	public TaskCompletionSource<E<CommandError>> ConnectEvent { get; }
	public TaskCompletionSource<object?> DisconnectEvent { get; }

	public ConnectionContext(ConnectionDataFull connectionDataFull)
	{
		// Note: TCS.SetResult can continue to run the code of the 'await TSC.Task'
		// somewhere else synchronously.
		// While the TsFullClient class is designed to be resistend to problems regarding
		// intermediate state changes with such call, we still add the runasync Task
		// option for a more consistent processing order and better predictable behaviour.
		ConnectEvent = new TaskCompletionSource<E<CommandError>>(TaskCreationOptions.RunContinuationsAsynchronously);
		DisconnectEvent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		TsCrypt = new TsCrypt(connectionDataFull.Identity);
		PacketHandler = new PacketHandler<S2C, C2S>(TsCrypt, connectionDataFull.LogId);
		ConnectionDataFull = connectionDataFull;
	}

	public uint NextReturnCode() => unchecked(++returnCode);
}
