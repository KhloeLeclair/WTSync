using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Websocket.Client;

using WTSync.Models;

namespace WTSync;

public sealed class SyncSocketClient : IDisposable {

	private readonly Plugin Plugin;

	private readonly WebsocketClient webSocket;

	public ulong[] IDs { get; }

	private TaskCompletionSource initialDataLoaded;

	private ConcurrentQueue<WTStatusAndId> updateQueue;

	public SyncSocketClient(Plugin plugin, IEnumerable<ulong> ids) {
		Plugin = plugin;
		IDs = ids.ToArray();

		initialDataLoaded = new TaskCompletionSource();
		updateQueue = new();

		webSocket = new WebsocketClient(
			new Uri(Url),
			() => {
				var client = new ClientWebSocket();
				client.Options.SetRequestHeader("User-Agent", $"WTSync/1.0");
				return client;
			}
		);

		webSocket.ReconnectTimeout = null; // TimeSpan.FromSeconds(1);
		webSocket.MessageReceived.Subscribe(OnMessage);
		webSocket.DisconnectionHappened.Subscribe(OnError);
		webSocket.ReconnectionHappened.Subscribe(OnReconnect);

		webSocket.Start();
	}

	public bool IsConnected => webSocket.IsRunning;

	public void Dispose() {
		webSocket.Dispose();
		initialDataLoaded.TrySetCanceled();
	}

	private void OnReconnect(ReconnectionInfo info) {
		Service.Logger.Debug($"Connected to server. ({info.Type})");
	}

	private void OnError(DisconnectionInfo info) {
		Service.Logger.Debug($"Disconnected from server. ({info.Type})");
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

					initialDataLoaded.TrySetResult();
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
