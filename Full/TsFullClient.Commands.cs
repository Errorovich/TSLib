// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Linq;
using System.Threading.Tasks;
using TSLib.Commands;
using TSLib.Messages;
using TSLib.Shared;
using CmdR = System.Threading.Tasks.Task<System.E<TSLib.Messages.CommandError>>;

namespace TSLib.Full;

// Командные обёртки, специфичные для полного клиента. Все они ходят через
// Send/SendNotifyCommand и потому потокобезопасны (см. контракт в TsFullClient.cs).
partial class TsFullClient
{
	public CmdR ChangeIsChannelCommander(bool isChannelCommander)
		=> SendVoid(new TsCommand("clientupdate") {
			{ "client_is_channel_commander", isChannelCommander },
		});

	public CmdR ChangeDescription(string newDescription)
		=> ChangeDescription(newDescription, ClientId);

	public CmdR RequestTalkPower(string? message = null)
		=> SendVoid(new TsCommand("clientupdate") {
			{ "client_talk_request", true },
			{ "client_talk_request_msg", message },
		});

	public CmdR CancelTalkPowerRequest()
		=> SendVoid(new TsCommand("clientupdate") {
			{ "client_talk_request", false },
		});

	public Task ClientDisconnect(Reason reason, string reasonMsg)
		=> SendNoResponsed(new TsCommand("clientdisconnect") {
			{ "reasonid", (int)reason },
			{ "reasonmsg", reasonMsg }
		});

	public CmdR ChannelSubscribeAll()
		=> SendVoid(new TsCommand("channelsubscribeall"));

	public CmdR ChannelUnsubscribeAll()
		=> SendVoid(new TsCommand("channelunsubscribeall"));

	public Task PokeClient(string message, ClientId clientId)
		=> SendNoResponsed(new TsCommand("clientpoke") {
			{ "clid", clientId },
			{ "msg", message },
		});

	public CmdR ChannelGroupAddClient(ChannelGroupId groupId, ChannelId channelId, ClientDbId clientDbId)
		=> SendVoid(new TsCommand("setclientchannelgroup") {
		{ "cgid", groupId },
		{ "cid", channelId },
		{ "cldbid", clientDbId },
			});

	public async Task<R<ClientConnectionInfo, CommandError>> GetClientConnectionInfo(ClientId clientId)
	{
		var result = await SendNotifyCommand(new TsCommand("getconnectioninfo") {
			{ "clid", clientId }
		}, NotificationType.ClientConnectionInfo);
		if (!result.Ok)
			return result.Error;
		return result.Value.Notifications
			.Cast<ClientConnectionInfo>()
			.Where(x => x.ClientId == clientId)
			.MapToSingle();
	}

	public async Task<R<ClientUpdated, CommandError>> GetClientVariables(ushort clientId)
		=> await SendNotifyCommand(new TsCommand("clientgetvariables") {
			{ "clid", clientId }
		}, NotificationType.ClientUpdated).MapToSingle<ClientUpdated>();

	public Task<R<ServerUpdated, CommandError>> GetServerVariables()
		=> SendNotifyCommand(new TsCommand("servergetvariables"),
			NotificationType.ServerUpdated).MapToSingle<ServerUpdated>();

	public CmdR SendPluginCommand(string name, string data, PluginTargetMode targetmode)
		=> SendVoid(new TsCommand("plugincmd") {
			{ "name", name },
			{ "data", data },
			{ "targetmode", (int)targetmode },
		});

	// Splitted base commands

	public override async Task<R<IChannelCreateResponse, CommandError>> ChannelCreate(string name,
		string? namePhonetic = null, string? topic = null, string? description = null, string? password = null,
		Codec? codec = null, int? codecQuality = null, int? codecLatencyFactor = null, bool? codecEncrypted = null,
		int? maxClients = null, int? maxFamilyClients = null, bool? maxClientsUnlimited = null,
		bool? maxFamilyClientsUnlimited = null, bool? maxFamilyClientsInherited = null, ChannelId? order = null,
		ChannelId? parent = null, ChannelType? type = null, TimeSpan? deleteDelay = null, int? neededTalkPower = null)
	{
		var result = await SendNotifyCommand(ChannelOp("channelcreate", null, name, namePhonetic, topic, description,
			  password, codec, codecQuality, codecLatencyFactor, codecEncrypted,
			  maxClients, maxFamilyClients, maxClientsUnlimited, maxFamilyClientsUnlimited,
			  maxFamilyClientsInherited, order, parent, type, deleteDelay, neededTalkPower),
			  NotificationType.ChannelCreated);
		return result.UnwrapNotification<ChannelCreated>()
			  .MapToSingle()
			  .WrapInterface<ChannelCreated, IChannelCreateResponse>();
	}

	public override async Task<R<ServerGroupAddResponse, CommandError>> ServerGroupAdd(string name, GroupType? type = null)
	{
		var result = await SendNotifyCommand(new TsCommand("servergroupadd") {
			{ "name", name },
			{ "type", (int?)type }
		}, NotificationType.ServerGroupList);
		if (!result.Ok)
			return result.Error;
		return result.Value.Notifications
			.Cast<ServerGroupList>()
			.Where(x => x.Name == name)
			.Take(1)
			.Select(x => new ServerGroupAddResponse() { ServerGroupId = x.ServerGroupId })
			.MapToSingle();
	}

	public override async Task<R<FileUpload, CommandError>> FileTransferInitUpload(ChannelId channelId, string path,
		string channelPassword, ushort clientTransferId, long fileSize, bool overwrite, bool resume)
	{
		var result = await SendNotifyCommand(new TsCommand("ftinitupload") {
			{ "cid", channelId },
			{ "name", path },
			{ "cpw", channelPassword },
			{ "clientftfid", clientTransferId },
			{ "size", fileSize },
			{ "overwrite", overwrite },
			{ "resume", resume }
		}, NotificationType.FileUpload, NotificationType.FiletransferStatus);
		if (!result.Ok)
			return result.Error;
		if (result.Value.NotifyType == NotificationType.FileUpload)
			return result.MapToSingle<FileUpload>();
		else
		{
			var ftresult = result.MapToSingle<FiletransferStatus>();
			if (!ftresult)
				return ftresult.Error;
			return new CommandError() { Id = ftresult.Value.Status, Message = ftresult.Value.Message };
		}
	}

	public override async Task<R<FileDownload, CommandError>> FileTransferInitDownload(ChannelId channelId,
		string path, string channelPassword, ushort clientTransferId, long seek)
	{
		var result = await SendNotifyCommand(new TsCommand("ftinitdownload") {
			{ "cid", channelId },
			{ "name", path },
			{ "cpw", channelPassword },
			{ "clientftfid", clientTransferId },
			{ "seekpos", seek } }, NotificationType.FileDownload, NotificationType.FiletransferStatus);
		if (!result.Ok)
			return result.Error;
		if (result.Value.NotifyType == NotificationType.FileDownload)
			return result.MapToSingle<FileDownload>();
		else
		{
			var ftresult = result.MapToSingle<FiletransferStatus>();
			if (!ftresult)
				return ftresult.Error;
			return new CommandError() { Id = ftresult.Value.Status, Message = ftresult.Value.Message };
		}
	}
}
