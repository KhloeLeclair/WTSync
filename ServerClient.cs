using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using WTSync.Models;

namespace WTSync;

internal class ServerClient : IDisposable {

	public static readonly JsonSerializerOptions JSON_OPTIONS = new() {
		//NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString,
		AllowTrailingCommas = true,
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly Plugin Plugin;
	private readonly HttpClient Client;

	private readonly Dictionary<string, WTStatus?> PendingStatus = [];
	private Task? UploadTask;

	internal bool isLoggingOut;

	internal string Version;

	public bool HasPendingUpdate => UploadTask is not null;

	private string? _LastError;

	public string? LastError => _LastError;

	public bool HasError => LastError is not null;

	internal ServerClient(Plugin plugin) {
		Plugin = plugin;
		Client = new HttpClient();
		Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";

		Client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("WTSync", Version));
	}

	public void Dispose() {
		UploadTask?.Dispose();
		UploadTask = null;

		Client.Dispose();
	}

	public SyncSocketClient StartStatusFeed(IEnumerable<string> ids) {
		return new SyncSocketClient(Plugin, ids);
	}

	public void PostUpdate(string id, WTStatus? status) {
		isLoggingOut = false;

		lock (PendingStatus) {
			PendingStatus[id] = status;
		}

		MaybeScheduleSubmission();
	}

	public void HandleLogout(IEnumerable<string> ids) {
		isLoggingOut = true;

		lock (PendingStatus) {
			PendingStatus.Clear();

			foreach (string id in ids)
				PendingStatus[id] = null;
		}

		MaybeScheduleSubmission();
	}

	private void MaybeScheduleSubmission() {
		int count;
		lock (PendingStatus) {
			count = PendingStatus.Count;
		}

		if (count > 0)
			UploadTask ??= SubmitUpdates().ContinueWith(t => {
				if (t.IsFaulted)
					Service.Logger.Error($"Unexpected error submitting state to server: {t.Exception}");
				UploadTask = null;
				MaybeScheduleSubmission();
			});
	}

	public async Task<ShareResult> MakeShareLink() {

		if (!Service.ClientState.IsLoggedIn || Plugin.PartyMemberTracker.Members.Count <= 0)
			return new(false, "Not logged in.", null);

		// We want to abbreviate player names so they aren't shown in full on
		// a share. This way it just shows the first and last initial, which
		// should be plenty for most situations.
		var data = new ShareRequest();
		foreach (var member in Plugin.PartyMemberTracker.Members) {
			data.Members.Add(new(
				Helpers.Abbreviate(member.Name, 3),
				member.Id
			));
		}

		try {
			Service.Logger.Debug($"Submitting request for share link to server.");
			var response = await Client.PostAsJsonAsync($"{Plugin.Config.ServerUrl}/api/share", data, JSON_OPTIONS);
			var respData = await response.Content.ReadFromJsonAsync<ShareResponse>();

			if (respData != null && respData.Ok)
				return new(true, null, respData.Url);

		} catch (Exception ex) {
			Service.Logger.Error($"Error requesting share link from server: {ex}");
		}

		return new(false, "The server returned an unexpected response. Try again in a bit.", null);

	}

	public async Task SubmitUpdates() {
		// First, make sure to let some time pass in case the user does more things.
		if (!isLoggingOut)
			await Task.Delay(5000);

		// Now, get the updates to send.
		List<WTStatusAndId> entries = [];
		lock (PendingStatus) {
			foreach (var entry in PendingStatus) {
				entries.Add(new() {
					Id = entry.Key,
					Anonymous = Plugin.Config.OptOutAnalytics,
					Status = entry.Value
				});
			}
			PendingStatus.Clear();
		}

		foreach (var entry in entries) {
			try {
				Service.Logger.Debug($"Submitting update for {entry.Id} to server.");
				var response = await Client.PostAsJsonAsync($"{Plugin.Config.ServerUrl}/api/submit", entry, JSON_OPTIONS);

				if (!response.IsSuccessStatusCode) {
					string state = await response.Content.ReadAsStringAsync();
					Service.Logger.Error($"Error submitting state to server: {state}");
					_LastError = state;

					if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
						lock (PendingStatus) {
							if (!PendingStatus.ContainsKey(entry.Id))
								PendingStatus[entry.Id] = entry.Status;
						}
				} else
					_LastError = null;

			} catch (Exception ex) {
				Service.Logger.Error($"Error submitting state to server: {ex}");
			}
		}
	}

}

internal record PartyResponse {
	public bool Ok { get; set; }
	public int Status { get; set; }
	public string? Message { get; set; }
	public WTStatusAndId[]? Results { get; set; }
}
