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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TSLib.Audio;
using TSLib.Commands;
using TSLib.Full.Book;
using TSLib.Helper;
using TSLib.Messages;
using TSLib.Scheduler;
using CmdR = System.Threading.Tasks.Task<System.E<TSLib.Messages.CommandError>>;

namespace TSLib.Full;

/// <summary>Creates a full TeamSpeak3 client with voice capabilities.</summary>
/// <remarks>
/// Потоковый контракт:
/// <list type="bullet">
/// <item><see cref="Connect"/>, <see cref="Disconnect"/> и все командные методы (Send*, обёртки)
/// можно вызывать с любого потока — клиент сам маршалит вызов на свой планировщик.</item>
/// <item>Все события поднимаются на потоке планировщика; <see cref="Book"/> читать только там
/// (снаружи — через <see cref="Invoke{T}(Func{T})"/>).</item>
/// <item>Голосовой путь (<see cref="Write"/>, <see cref="SendAudio"/> и родственные) не маршалится —
/// он потокобезопасен сам по себе и зовётся напрямую с аудио-потоков.</item>
/// </list>
/// </remarks>
public sealed partial class TsFullClient : TsBaseFunctions, IAudioActiveProducer, IAudioPassiveConsumer
{
	private readonly AsyncMessageProcessor msgProc;
	private readonly DedicatedTaskScheduler scheduler;
	private readonly bool isOwnScheduler;

	private volatile ConnectionContext? context;
	private int disposed;

	public override ClientType ClientType => ClientType.Full;
	/// <summary>The client id given to this connection by the server.</summary>
	public ClientId ClientId => context?.PacketHandler.ClientId ?? ClientId.Null;
	/// <summary>The disonnect message when leaving.</summary>
	public string QuitMessage { get; set; } = "Disconnected";
	/// <summary>The <see cref="TsVersionSigned"/> used to connect.</summary>
	public TsVersionSigned? VersionSign => context?.ConnectionDataFull.VersionSign;
	/// <summary>The <see cref="IdentityData"/> used to connect.</summary>
	public IdentityData? Identity => context?.ConnectionDataFull.Identity;
	/// <summary>
	/// Status overview:
	/// <list type="bullet">
	/// <item> Disconnected:
	///   <para> ! PacketHandler is not initalized, context == null</para>
	///   <para> -> Connect() => Connecting</para>
	/// </item>
	/// <item> Connecting:
	///   <para> -> Init/Crypto-Error => Disconnected</para>
	///   <para> -> Timeout => Disconnected</para>
	///   <para> -> Final Init => Connected</para>
	/// </item>
	/// <item> Connected:
	///   <para> -> Timeout => Disconnected</para>
	///   <para> -> Kick/Leave => Disconnecting</para>
	/// </item>
	/// <item> Disconnecting:
	///   <para> -> Timeout => Disconnected</para>
	///   <para> -> Final Ack => Disconnected</para>
	/// </item>
	/// </list>
	/// </summary>
	private TsClientStatus status;
	public override bool Connected => status == TsClientStatus.Connected;
	public override bool Connecting => status == TsClientStatus.Connecting;
	protected override Deserializer Deserializer => msgProc.Deserializer;
	public Connection Book { get; } = new Connection();

	public override event EventHandler<DisconnectEventArgs>? OnDisconnected;
	public event EventHandler<CommandError>? OnErrorEvent;
	public event EventHandler<TsClientStatus>? OnStatusChangedEvent;

	/// <summary>Creates a new client. A client can manage one connection to a server.</summary>
	/// <param name="scheduler">The scheduler which will process all messages and events of this client.
	/// When null the client creates and owns a dedicated scheduler thread itself.</param>
	public TsFullClient(DedicatedTaskScheduler? scheduler = null)
	{
		status = TsClientStatus.Disconnected;
		msgProc = new AsyncMessageProcessor(MessageHelper.GetToClientNotificationType);
		this.scheduler = scheduler ?? new DedicatedTaskScheduler(Id.Null);
		this.isOwnScheduler = scheduler is null;
	}

	#region Invoke (доступ к состоянию клиента с чужого потока)

	/// <summary>Выполняет код на потоке планировщика клиента (например, чтение <see cref="Book"/>).
	/// Если вызвано уже с потока планировщика — выполняется сразу.</summary>
	public Task Invoke(Action action)
	{
		ThrowIfDisposed();
		return scheduler.Invoke(action);
	}

	/// <inheritdoc cref="Invoke(Action)"/>
	public Task<T> Invoke<T>(Func<T> func)
	{
		ThrowIfDisposed();
		return scheduler.Invoke(func);
	}

	/// <inheritdoc cref="Invoke(Action)"/>
	public Task InvokeAsync(Func<Task> func)
	{
		ThrowIfDisposed();
		return scheduler.InvokeAsync(func);
	}

	/// <inheritdoc cref="Invoke(Action)"/>
	public Task<T> InvokeAsync<T>(Func<Task<T>> func)
	{
		ThrowIfDisposed();
		return scheduler.InvokeAsync(func);
	}

	#endregion

	/// <summary>Tries to connect to a server. Can be called from any thread.</summary>
	/// <param name="conData">Set the connection information properties as needed.
	/// For further details about each setting see the respective property documentation in <see cref="ConnectionData"/></param>
	/// <exception cref="ArgumentException">When some required values are not set or invalid.</exception>
	/// <exception cref="TsException">When the connection could not be established.</exception>
	public override async CmdR Connect(ConnectionData conData)
	{
		if (conData is not ConnectionDataFull conDataFull) throw new ArgumentException($"Use the {nameof(ConnectionDataFull)} derivative to connect with the full client.", nameof(conData));
		if (conDataFull.Identity is null) throw new ArgumentNullException(nameof(conDataFull.Identity));
		if (conDataFull.VersionSign is null) throw new ArgumentNullException(nameof(conDataFull.VersionSign));
		ThrowIfDisposed();

		return await scheduler.InvokeAsync(() => ConnectInner(conDataFull));
	}

	private async CmdR ConnectInner(ConnectionDataFull conData)
	{
		await DisconnectInner();

		remoteAddress = await TsDnsResolver.TryResolve(conData.Address);
		if (remoteAddress is null)
			return CommandError.Custom("Could not read or resolve address.");

		ConnectionData = conData;
		ServerConstants = TsConst.Default;
		Book.Reset();

		var ctx = new ConnectionContext(conData);
		context = ctx;

		ctx.PacketHandler.PacketEvent = (ref Packet<S2C> packet) =>
		{
			if (status == TsClientStatus.Disconnected)
				return;
			PacketEvent(ctx, ref packet);
		};
		ctx.PacketHandler.StopEvent = (closeReason) =>
		{
			_ = scheduler.Invoke(() =>
			{
				ctx.ExitReason ??= closeReason;
				ChangeState(ctx, TsClientStatus.Disconnected);
			});
		};

		ChangeState(ctx, TsClientStatus.Connecting);
		if (!ctx.PacketHandler.Connect(remoteAddress).GetOk(out var error))
		{
			ChangeState(ctx, TsClientStatus.Disconnected);
			return CommandError.Custom(error);
		}
		return await ctx.ConnectEvent.Task; // TODO check error state
	}

	/// <summary>
	/// Disconnects from the current server and closes the connection.
	/// Does nothing if the client is not connected. Can be called from any thread.
	/// </summary>
	public override Task Disconnect()
	{
		if (disposed != 0)
			return Task.CompletedTask;
		return scheduler.InvokeAsync(DisconnectInner);
	}

	private async Task DisconnectInner()
	{
		var ctx = context;
		if (ctx is null)
			return;

		// TODO: Consider if it is better when in connecting state to wait for connect completion then disconnect
		if (status == TsClientStatus.Connected)
		{
			await ClientDisconnect(Reason.LeftServer, QuitMessage);
			ChangeState(ctx, TsClientStatus.Disconnecting);
		}
		else
		{
			ChangeState(ctx, TsClientStatus.Disconnected);
		}
		await ctx.DisconnectEvent.Task;
	}

	private void ChangeState(ConnectionContext ctx, TsClientStatus setStatus, CommandError? error = null)
	{
		scheduler.VerifyOwnThread();

		if (ctx != context)
			Log.Debug("Stray disconnect from old packethandler");

		Log.Debug("ChangeState {0} -> {1} (error:{2})", status, setStatus, error?.ErrorFormat() ?? "none");

		switch ((status, setStatus))
		{
		case (TsClientStatus.Disconnected, TsClientStatus.Disconnected):
			// Already disconnected, do nothing
			break;

		case (TsClientStatus.Disconnected, TsClientStatus.Connecting):
			status = TsClientStatus.Connecting;
			break;

		case (TsClientStatus.Connecting, TsClientStatus.Connected):
			status = TsClientStatus.Connected;
			ctx.ConnectEvent.SetResult(R.Ok);
			break;

		case (TsClientStatus.Connecting, TsClientStatus.Disconnected):
		case (TsClientStatus.Connected, TsClientStatus.Disconnected):
		case (TsClientStatus.Disconnecting, TsClientStatus.Disconnected):
			// Запоминаем статус ДО перезаписи: иначе ветка Connecting ниже никогда не сработает
			// и ConnectEvent не завершится — Connect() при неудачном подключении зависнет навсегда.
			var statusBefore = status;
			status = TsClientStatus.Disconnected;
			ctx.PacketHandler.Stop();
			msgProc.DropQueue();

			context = null;
			if (statusBefore == TsClientStatus.Connecting)
				ctx.ConnectEvent.SetResult(error ?? CommandError.ConnectionClosed); // TODO: Set exception maybe ?
			ctx.DisconnectEvent.SetResult(null);
			OnDisconnected?.Invoke(this, new DisconnectEventArgs(ctx.ExitReason ?? Reason.LeftServer, error));
			break;

		case (TsClientStatus.Connected, TsClientStatus.Disconnecting):
			status = TsClientStatus.Disconnecting;
			break;

		default:
			Trace.Fail($"Invalid transition change from {status} to {setStatus}");
			break;
		}

		OnStatusChangedEvent?.Invoke(this, status);
    }

	private void PacketEvent(ConnectionContext ctx, ref Packet<S2C> packet)
	{
		switch (packet.PacketType)
		{
		case PacketType.Command:
		case PacketType.CommandLow:
			var data = packet.Data;
			if (Log.IsDebugEnabled)
				Log.Debug("[I] {0}", Tools.Utf8Encoder.GetString(packet.Data));
			_ = scheduler.Invoke(() =>
			{
				if (ctx != context)
					Log.Debug("Stray packet from old packethandler");

				var result = msgProc.PushMessage(data);
				if (result != null)
					InvokeEvent(result.Value);
			});
			break;

		case PacketType.Voice:
		case PacketType.VoiceWhisper:
		{
			Span<byte> packetData = packet.Data.AsSpan();
			OutStream?.Write(
				packetData.Slice(VoicePacket.InHeaderSize),
				VoicePacket.ParseInMeta(packetData, packet.PacketType == PacketType.VoiceWhisper));
			break;
		}

		case PacketType.Init1:
			// Init error
			if (packet.Data.Length == 5 && packet.Data[0] == 1)
			{
				var errorNum = BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.AsSpan(1));
				if (!Enum.IsDefined(typeof(TsErrorCode), errorNum))
					Log.Info("Got init error: {0}", (TsErrorCode)errorNum);
				else
					Log.Warn("Got undefined init error: {0}", errorNum);
				_ = scheduler.Invoke(() => ChangeState(ctx, TsClientStatus.Disconnected));
			}
			break;
		}
	}

	// Local event processing
	// Форвардеры генерируемой диспетчеризации (InvokeEvent в TsFullClient.gen.cs). Нотификация,
	// пришедшая после дисконнекта (context == null), молча игнорируется — это не ошибка.

	async partial void ProcessEachInitIvExpand(InitIvExpand notify)
	{
		if (context is not { } ctx) return;

		ctx.PacketHandler.ReceivedFinalInitAck();

		var result = FullClientHandshake.CryptoInit(ctx, notify);
		if (!result)
		{
			ChangeState(ctx, TsClientStatus.Disconnected, CommandError.Custom($"Failed to calculate shared secret: {result.Error}"));
			return;
		}

		await SendNoResponsed(FullClientHandshake.BuildClientInit(ctx.ConnectionDataFull));
	}

	async partial void ProcessEachInitIvExpand2(InitIvExpand2 notify)
	{
		if (context is not { } ctx) return;

		ctx.PacketHandler.ReceivedFinalInitAck();

		var (clientEk, tempPrivateKey) = FullClientHandshake.BuildClientEk(ctx, notify);
		await SendNoResponsed(clientEk);

		var result = FullClientHandshake.CryptoInit2(ctx, notify, tempPrivateKey);
		if (!result)
		{
			ChangeState(ctx, TsClientStatus.Disconnected, CommandError.Custom($"Failed to calculate shared secret: {result.Error}"));
			return;
		}

		await SendNoResponsed(FullClientHandshake.BuildClientInit(ctx.ConnectionDataFull));
	}

	partial void ProcessEachInitServer(InitServer notify)
	{
		if (context is not { } ctx) return;

		ctx.PacketHandler.ClientId = notify.ClientId;
		var serverVersion = TsVersion.TryParse(notify.Version, notify.Platform);
		if (serverVersion != null)
			ServerConstants = TsConst.GetByServerBuildNum(serverVersion.Build);

		ChangeState(ctx, TsClientStatus.Connected);
	}

	async partial void ProcessEachPluginCommand(PluginCommand notify)
	{
		if (notify.Name == "cliententerview" && notify.Data == "version")
			await SendPluginCommand("cliententerview", "TAB", PluginTargetMode.Server);
	}

	partial void ProcessEachCommandError(CommandError notify)
	{
		if (status == TsClientStatus.Connecting)
		{
			if (context is { } ctx)
				ChangeState(ctx, TsClientStatus.Disconnected, notify);
		}
		else
		{
			OnErrorEvent?.Invoke(this, notify);
		}
	}

	partial void ProcessEachClientLeftView(ClientLeftView notify)
	{
		if (context is not { } ctx) return;

		if (notify.ClientId == ctx.PacketHandler.ClientId)
		{
			ctx.ExitReason = notify.Reason;
			ChangeState(ctx, TsClientStatus.Disconnected);
		}
	}

	async partial void ProcessEachChannelListFinished(ChannelListFinished notify)
	{
		await ChannelSubscribeAll();
		await PermissionList();
	}

	async partial void ProcessEachClientConnectionInfoUpdateRequest(ClientConnectionInfoUpdateRequest notify)
	{
		if (context is not { } ctx) return;

		await SendNoResponsed(ctx.PacketHandler.NetworkStats.GenerateStatusAnswer());
	}

	partial void ProcessPermList(PermList[] notifies)
	{
		var buildPermissions = new List<TsPermission>(notifies.Length + 1) { TsPermission.undefined };
		foreach (var perm in notifies)
		{
			if (!string.IsNullOrEmpty(perm.PermissionName))
			{
				if (Enum.TryParse<TsPermission>(perm.PermissionName, out var tsPerm))
					buildPermissions.Add(tsPerm);
				else
					buildPermissions.Add(TsPermission.undefined);
			}
		}
		Deserializer.PermissionTransform = new TablePermissionTransform(buildPermissions.ToArray());
	}

	// ***

	/// <summary>
	/// Sends a command without expecting a 'error' return code.
	/// <para>NOTE: Do not use this method unless you are sure the ts3 command fits the criteria.</para>
	/// </summary>
	/// <param name="command">The command to send.</param>
	public Task SendNoResponsed(TsCommand command)
	{
		return SendVoid(command.ExpectsResponse(false));
	}

	/// <summary>
	/// Sends a command to the server. Commands look exactly like query commands and mostly also behave identically.
	/// Can be called from any thread.
	/// <para>NOTE: Do not expect all commands to work exactly like in the query documentation.</para>
	/// </summary>
	/// <typeparam name="T">The type to deserialize the response to. Use <see cref="ResponseDictionary"/> for unknow response data.</typeparam>
	/// <param name="com">The command to send.
	/// <para>NOTE: By default does the command expect an answer from the server. Set <see cref="TsCommand.ExpectResponse"/> to false
	/// if the client hangs after a special command (<see cref="Send{T}(TsCommand)"/> will return a generic error instead).</para></param>
	/// <returns>Returns <code>R(OK)</code> with an enumeration of the deserialized and split up in <see cref="T"/> objects data.
	/// Or <code>R(ERR)</code> with the returned error if no response is expected.</returns>
	public override Task<R<T[], CommandError>> Send<T>(TsCommand com)
	{
		if (disposed != 0)
			return Task.FromResult<R<T[], CommandError>>(CommandError.ConnectionClosed);
		return scheduler.InvokeAsync(() => SendInner<T>(com));
	}

	private async Task<R<T[], CommandError>> SendInner<T>(TsCommand com) where T : IResponse, new()
	{
		using var wb = new WaitBlock(msgProc.Deserializer);
		var result = SendCommandBase(wb, com);
		if (!result.Ok)
			return result.Error;
		if (com.ExpectResponse)
			return await wb.WaitForMessageAsync<T>();
		else
			// This might not be the nicest way to return in this case
			// but we don't know what the response is, so this acceptable.
			return CommandError.NoResult;
	}

	public override async Task<R<T[], CommandError>> SendHybrid<T>(TsCommand com, NotificationType type)
	{
		var notification = await SendNotifyCommand(com, type);
		return notification.UnwrapNotification<T>();
	}

	public Task<R<LazyNotification, CommandError>> SendNotifyCommand(TsCommand com, params NotificationType[] dependsOn)
	{
		if (!com.ExpectResponse)
			throw new ArgumentException("A special command must take a response");
		if (disposed != 0)
			return Task.FromResult<R<LazyNotification, CommandError>>(CommandError.ConnectionClosed);
		return scheduler.InvokeAsync(() => SendNotifyCommandInner(com, dependsOn));
	}

	private async Task<R<LazyNotification, CommandError>> SendNotifyCommandInner(TsCommand com, NotificationType[] dependsOn)
	{
		using var wb = new WaitBlock(msgProc.Deserializer, dependsOn);
		var result = SendCommandBase(wb, com);
		if (!result.Ok)
			return result.Error;
		return await wb.WaitForNotificationAsync();
	}

	private E<CommandError> SendCommandBase(WaitBlock wb, TsCommand com)
	{
		scheduler.VerifyOwnThread();

		if (status != TsClientStatus.Connecting && status != TsClientStatus.Connected)
			return CommandError.ConnectionClosed;

		if (context is not { } ctx)
			return CommandError.ConnectionClosed;

		if (com.ExpectResponse)
		{
			var retCodeParameter = new CommandParameter("return_code", ctx.NextReturnCode());
			com.Add(retCodeParameter);
			msgProc.EnqueueRequest(retCodeParameter.Value, wb);
		}

		var message = com.ToString();
		Log.Debug("[O] {0}", message);
		byte[] data = Tools.Utf8Encoder.GetBytes(message);
		var sendResult = ctx.PacketHandler.AddOutgoingPacket(data, PacketType.Command);
		if (!sendResult)
			Log.Debug("packetHandler couldn't send packet: {0}", sendResult.Error);
		return R.Ok;
	}

	/// <summary>Release all resources. Does not wait for a normal disconnect. Await Disconnect for this instead.</summary>
	public override void Dispose()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 1)
			return;
		context?.PacketHandler.Stop();
		if (isOwnScheduler && scheduler is IDisposable disp)
			disp.Dispose();
	}

	private void ThrowIfDisposed()
	{
		if (disposed != 0)
			throw new ObjectDisposedException(nameof(TsFullClient));
	}

	#region Audio
	/// <summary>Receive voice packets.</summary>
	public IAudioPassiveConsumer? OutStream { get; set; }
	/// <summary>When voice data can be sent.</summary>
	// TODO may set to false if no talk power, etc.
	public bool Active => true;
	/// <summary>Send voice data.</summary>
	/// <param name="data">The encoded audio buffer.</param>
	/// <param name="meta">The metadata where to send the packet.</param>
	public void Write(Span<byte> data, Meta? meta)
	{
		if (meta?.Out is null
			|| meta.Out.SendMode == TargetSendMode.None
			|| !meta.Codec.HasValue
			|| meta.Codec.Value == Codec.Raw)
			return;

		switch (meta.Out.SendMode)
		{
		case TargetSendMode.None:
			break;
		case TargetSendMode.Voice:
			SendAudio(data, meta.Codec.Value);
			break;
		case TargetSendMode.Whisper:
			SendAudioWhisper(data, meta.Codec.Value, meta.Out.ChannelIds!, meta.Out.ClientIds!);
			break;
		case TargetSendMode.WhisperGroup:
			SendAudioGroupWhisper(data, meta.Codec.Value, meta.Out.GroupWhisperType, meta.Out.GroupWhisperTarget, meta.Out.TargetId);
			break;
		default: throw Tools.UnhandledDefault(meta.Out.SendMode);
		}
	}

	public void SendAudio(in ReadOnlySpan<byte> data, Codec codec)
	{
		if (context is { } ctx)
			VoicePacket.SendVoice(ctx.PacketHandler, data, codec);
	}

	public void SendAudioWhisper(in ReadOnlySpan<byte> data, Codec codec, IReadOnlyList<ChannelId> channelIds, IReadOnlyList<ClientId> clientIds)
	{
		if (context is { } ctx)
			VoicePacket.SendWhisper(ctx.PacketHandler, data, codec, channelIds, clientIds);
	}

	public void SendAudioGroupWhisper(in ReadOnlySpan<byte> data, Codec codec, GroupWhisperType type, GroupWhisperTarget target, ulong targetId = 0)
	{
		if (context is { } ctx)
			VoicePacket.SendGroupWhisper(ctx.PacketHandler, data, codec, type, target, targetId);
	}
	#endregion
}
