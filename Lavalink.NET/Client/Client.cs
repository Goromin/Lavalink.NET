﻿using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Lavalink.NET.Types;
using Serilog;
using Lavalink.NET.Websocket;
using System.Threading;
using Lavalink.NET.Player;

namespace Lavalink.NET
{
	/// <summary>
	/// ClientOptions to instanciate the Client with.
	/// </summary>
	public class ClientOptions
	{
		/// <summary>
		/// Host for the Rest API, includes protocol, hostname and port.
		/// </summary>
		public string HostRest { internal get; set; }

		/// <summary>
		/// Host for the Websocket API, includes protocol, hostname and port.
		/// </summary>
		public string HostWS { internal get; set; }

		/// <summary>
		/// Password for your lavalink node.
		/// </summary>
		public string Password { internal get; set; }

		/// <summary>
		/// The UserID of your Bot User.
		/// </summary>
		public ulong UserID { internal get; set; }

		/// <summary>
		/// determine if the build in Logging module should be used.
		/// </summary>
		public bool UseLogging { internal get; set; } = false;

		/// <summary>
		/// The LogLevel of this Client.
		/// </summary>
		public LogLevel LogLevel { internal get; set; } = LogLevel.Info;
	}

	/// <summary>
	/// Base Client to start interacting with Lavalink.
	/// </summary>
	public abstract class Client
	{
		/// <summary>
		/// Event that gets triggerd when a Message from Lavalink is received.
		/// </summary>
		public event MessageEvent Message;

		/// <summary>
		/// Event that gets triggerd when this Client is ready to use.
		/// </summary>
		public event EventHandler Ready;

		/// <summary>
		/// Event that gets triggerd when this Client Websocket Connection Disconnects.
		/// </summary>
		public event CloseEvent Disconnect;

		/// <summary>
		/// Event that gets triggerd when this Client encounters an Error.
		/// </summary>
		public event ErrorEvent Error;

		/// <summary>
		/// Event what emits debug messages.
		/// </summary>
		public event DebugEvent Debug;

		/// <summary>
		/// Event that gets triggerd when a Stats Message from Lavalink is received.
		/// </summary>
		public event StatsEvent Stats;

		/// <summary>
		/// The Store of all Players from this Client.
		/// </summary>
		public PlayerStore Players { get; }

		/// <summary>
		/// The Store for all VoiceStates.
		/// </summary>
		internal Dictionary<ulong, string> VoiceStates = new Dictionary<ulong, string>();

		/// <summary>
		/// The Store for all VoiceServers.
		/// </summary>
		internal Dictionary<ulong, VoiceServerUpdate> VoiceServers = new Dictionary<ulong, VoiceServerUpdate>();

		/// <summary>
		/// The Websocket instance for this Client.
		/// </summary>
		internal Websocket.Websocket Websocket { get; set; }

		/// <summary>
		/// The Logger instance for this client.
		/// </summary>
		private ILogger Logger { get; }

		/// <summary>
		/// The config for this instance.
		/// </summary>
		private readonly ClientOptions _config;

		/// <summary>
		/// Creates a new Client instance.
		/// </summary>
		/// <param name="options"> Options to instance the Client with. </param>
		public Client(ClientOptions options)
		{
			_config = options;
			Websocket = new Websocket.Websocket(new WebsocketOptions(_config.HostWS, _config.Password, _config.UserID.ToString()));
			Players = new PlayerStore(this);

			if (options.UseLogging) Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(new Serilog.Core.LoggingLevelSwitch( (Serilog.Events.LogEventLevel) _config.LogLevel)).WriteTo.Console().CreateLogger();


			Debug += DebugHandler;
			Ready += ReadyHandler;
			Disconnect += DisconnectHandler;
			Websocket.Message += WebsocketMessage;
			Websocket.Ready += Ready;
			Websocket.Debug += Debug;
			Websocket.Close += Disconnect;
			Websocket.ConnectionFailed += ConnectionFailedHandler;
		}

		/// <summary>
		/// Abstracted method you need to implement yourself, this method should either use the data to connect to a VoiceChannel externally or serialize to a string and send to the Discord Websocket.
		/// </summary>
		/// <param name="voicePacket"> The actuall packet this can be used to connect external or directly send to the Websocket. </param>
		/// <returns> Task resolving with void. </returns>
		public abstract Task SendAsync(DiscordOP4Packet voicePacket);

		/// <summary>
		/// Method to Connect to the Lavalink Websocket.
		/// </summary>
		/// <returns> void. </returns>
		public void Start()
		{
			Thread websocketThread = new Thread(new ThreadStart(Websocket.ConnectAsync));
			websocketThread.Start();
		}

		/// <summary>
		/// Method to Load tracks from the Lavalink Rest Api.
		/// </summary>
		/// <param name="query"> The Search Parameter for the track, eg. an URL, an local path or an string. </param>
		/// <returns> An Array of tracks the Rest API returns, can be empty if no result. </returns>
		public async Task<List<Track>> LoadTracksAsync(string query)
		{
			HttpWebRequest request = (HttpWebRequest) WebRequest.Create(_config.HostRest + "/loadtracks?identifier=" + query);
			
			request.Headers.Add("Authorization", _config.Password);
			request.Headers.Add("Content-Type", "application/json");
			request.Headers.Add("Accept", "application/json");

			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

			using (HttpWebResponse response = (HttpWebResponse) await request.GetResponseAsync())
			using (Stream stream = response.GetResponseStream())
			using (StreamReader reader = new StreamReader(stream))
			{
				string json = await reader.ReadToEndAsync();
				return JsonConvert.DeserializeObject<List<Track>>(json);
			}
		}

		/// <summary>
		/// Method to Load tracks from the Lavalink Rest Api.
		/// </summary>
		/// <param name="query"> The Search Parameter for the track, eg. an URL, an local path or an string. </param>
		/// <returns> An Array of tracks the Rest API returns, can be empty if no result was found. </returns>
		public async Task<List<Track>> LoadTracksAsync(Uri query)
		{
			HttpWebRequest request = (HttpWebRequest) WebRequest.Create(_config.HostRest + "/loadtracks?identifier=" + query.AbsoluteUri);

			request.Headers.Add("Authorization", _config.Password);
			request.Headers.Add("Content-Type", "application/json");
			request.Headers.Add("Accept", "application/json");

			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

			using (HttpWebResponse response = (HttpWebResponse) await request.GetResponseAsync())
			using (Stream stream = response.GetResponseStream())
			using (StreamReader reader = new StreamReader(stream))
			{
				string json = await reader.ReadToEndAsync();
				return JsonConvert.DeserializeObject<List<Track>>(json);
			}
		}

		/// <summary>
		/// Method to call on VoiceStateUpdates.
		/// </summary>
		/// <param name="packet"> VoiceStateUpdatePacket you get from the Discord API. </param>
		/// <returns> A Task resolving with a Bool what shows if the changes went through to the server. </returns>
		public Task<bool> VoiceStateUpdateAsync(VoiceStateUpdate packet)
		{
			if (packet.UserID != _config.UserID) return Task.FromResult(false);

			if (VoiceStates.ContainsKey(packet.GuildID))
			{
				VoiceStates[packet.GuildID] = packet.SessionID;
			} else
			{
				VoiceStates.Add(packet.GuildID, packet.SessionID);
			}

			return ConnectVoiceAsync(packet.GuildID);
		}

		/// <summary>
		/// Method to call on VoiceServerUpdate.
		/// </summary>
		/// <param name="packet"> VoiceServerUpdatePacket you get from the Discord API. </param>
		/// <returns> A Task resolving with a Bool what shows if the changes went through to the server. </returns>
		public Task<bool> VoiceServerUpdateAsync(VoiceServerUpdate packet)
		{
			if (VoiceServers.ContainsKey(packet.GuildID))
			{
				VoiceServers[packet.GuildID] = packet;
			} else
			{
				VoiceServers.Add(packet.GuildID, packet);
			}

			return ConnectVoiceAsync(packet.GuildID);
		}

		internal void EmitLogs(LogLevel level, string message)
		{
			if (!_config.UseLogging || _config.LogLevel > level) return;
			if (level == LogLevel.Debug) Logger.Debug(message);
			else if (level == LogLevel.Info) Logger.Information(message);
			else if (level == LogLevel.Warning) Logger.Warning(message);
			else Logger.Error(message);
		}

		private async Task<bool> ConnectVoiceAsync(ulong guildID)
		{
			VoiceStates.TryGetValue(guildID, out string state);
			VoiceServers.TryGetValue(guildID, out VoiceServerUpdate voiceServerUpdate);

			if (state == null || voiceServerUpdate == null) return await Task.FromResult(false);

			await Players.GetPlayer(guildID).VoiceUpdateAsync(state, voiceServerUpdate);

			return await Task.FromResult(true);
		}

		private void WebsocketMessage(object sender, MessageEventArgs e)
		{
			dynamic lavalinkEvent = JObject.Parse(e.Message);

			Debug(this, new DebugEventArgs($"Received Websocket message from Lavalink with OP \"{lavalinkEvent.op}\""));

			Message?.Invoke(this, new MessageEventArgs(lavalinkEvent));

			if (lavalinkEvent.op == "event")
			{
				if (lavalinkEvent.guildId != null)
				{
					Debug(this, new DebugEventArgs($"Received Player Event with GuildID {lavalinkEvent.guildId}, emit event on player."));
					Player.Player player = Players.GetPlayer(Convert.ToUInt64(lavalinkEvent.guildId));
					player.EmitEvent(lavalinkEvent);
				} else {
					Debug(this, new DebugEventArgs($"Received Lavalink event with \"event\" op but no guild id\n{lavalinkEvent}"));
				}
			} else if (lavalinkEvent.op == "stats")
			{
				Stats?.Invoke(this, JsonConvert.DeserializeObject<StatsEventArgs>(e.Message));
			} else if (lavalinkEvent.op == "playerUpdate")
			{
				Player.Player player = Players.GetPlayer(Convert.ToUInt64(lavalinkEvent.guildId));
				player.Position = Convert.ToUInt64(lavalinkEvent.state.position);
			}
		}

		private void ErrorHandler(object sender, Types.ErrorEventArgs args)
		{
			var message = $"Encountered following exeption while executing events {args.Error.Message}";
			EmitLogs(LogLevel.Error, message);
			Error?.Invoke(this, args);
		}

		private void DebugHandler(object sender, DebugEventArgs args) 
			=> EmitLogs(LogLevel.Debug, args.Message);

		private void ReadyHandler(object sender, EventArgs args)
			=> EmitLogs(LogLevel.Info, "LavalinkClient succesfully initialized");

		private void ConnectionFailedHandler(object sender, ConnectionFailedArgs args)
		{
			var message = $"Connection refused with following message \"{args.Exception.Message}\".";
			EmitLogs(LogLevel.Error, message);
			Error?.Invoke(this, new Types.ErrorEventArgs(new Exception(message)));
		}

		private void DisconnectHandler(object sender, CloseEventArgs args) 
			=> EmitLogs(LogLevel.Error, $"Websocket Connection Closed with following reason \"{args.Reason}\" and StatusCode \"{args.Status}\"");
	}
}