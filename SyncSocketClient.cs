using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;

using Websocket.Client;

using WTSync.Models;

namespace WTSync;

public sealed class SyncSocketClient : IDisposable {

	private readonly Plugin Plugin;

	private readonly WebsocketClient webSocket;

	public string[] IDs { get; }

	private readonly ConcurrentQueue<WTStatusAndId> updateQueue;

	public int ConnectedClients { get; private set; }


	public SyncSocketClient(Plugin plugin, IEnumerable<string> ids) {
		Plugin = plugin;
		IDs = ids.ToArray();

		updateQueue = new();

		webSocket = new WebsocketClient(
			new Uri(Url),
			() => {
				var client = new ClientWebSocket();
				client.Options.SetRequestHeader("User-Agent", $"WTSync/{Plugin.ServerClient.Version}");
				return client;
			}
		);

		webSocket.ReconnectTimeout = null;
		webSocket.MessageReceived.Subscribe(OnMessage);
		webSocket.DisconnectionHappened.Subscribe(OnError);
		webSocket.ReconnectionHappened.Subscribe(OnReconnect);

		webSocket.Start();
	}

	public bool IsConnected => webSocket.IsRunning;

	public void Dispose() {
		webSocket.Dispose();
	}

	public void SendStatusRequest() {
		if (webSocket.IsRunning)
			webSocket.Send("{\"msg\":\"get_status\"}");
	}

	private void OnReconnect(ReconnectionInfo info) {
		Service.Logger.Debug($"Connected to server. ({info.Type})");
		ConnectedClients = 0;
		SendStatusRequest();
	}

	private void OnError(DisconnectionInfo info) {
		Service.Logger.Debug($"Disconnected from server. ({info.Type})");
		ConnectedClients = 0;
	}

	private void OnMessage(ResponseMessage message) {
		if (message.MessageType != WebSocketMessageType.Text) {
			Service.Logger.Debug($"Unexpected message type: {message.MessageType}");
			return;
		}

		if (string.IsNullOrEmpty(message.Text))
			return;

		JsonObject? decoded;
		try {
			decoded = JsonNode.Parse(message.Text, null, new JsonDocumentOptions() { AllowTrailingCommas = true })?.AsObject();
		} catch (Exception ex) {
			Service.Logger.Debug($"Error decoding message from server: {ex}");
			return;
		}

		if (decoded != null) {
			string? type = decoded["msg"]?.ToString();
			switch (type) {
				case "initial":
					var results = decoded["results"]?.AsArray().Deserialize<List<WTStatusAndId>>(ServerClient.JSON_OPTIONS);
					if (results != null)
						foreach (var entry in results)
							updateQueue.Enqueue(entry);

					break;

				case "status":
					var result = decoded["connections"]?.AsValue();
					if (result != null)
						try {
							ConnectedClients = (int) result;
						} catch (Exception ex) {
#if DEBUG
							Service.Logger.Debug($"Error receiving status from server: {ex}");
#endif
						}
					break;

				case "update":
					var item = decoded["data"]?.Deserialize<WTStatusAndId>(ServerClient.JSON_OPTIONS);
					if (item != null)
						updateQueue.Enqueue(item);
					break;
			}
		}
	}

	public bool HasUpdate => !updateQueue.IsEmpty;

	public bool TryGetUpdate([NotNullWhen(true)] out WTStatusAndId? update) {
		return updateQueue.TryDequeue(out update);
	}

	public string Url {
		get {
			string idString = string.Join(',', IDs);
			string uri = $"{Plugin.Config.ServerUrl}/party/{idString}";
			if (uri.StartsWith("http:"))
				uri = $"ws:{uri[5..]}";
			else if (uri.StartsWith("https:"))
				uri = $"wss:{uri[6..]}";
			return uri;
		}
	}

}
