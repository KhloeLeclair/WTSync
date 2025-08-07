// Ignore Spelling: Plugin

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Dalamud;
using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Utility;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

using Lumina.Text;

using WTSync.Models;
using WTSync.UI;

namespace WTSync;

public sealed class Plugin : IDalamudPlugin {

	internal static Plugin Instance { get; private set; } = null!;

	internal Localization Localization { get; init; }

	internal Configuration Config { get; private set; }

	internal WindowSystem WindowSystem { get; } = new WindowSystem("WTSync");

	internal ClickableDuties ClickableDuties { get; private set; }

	internal MainWindow MainWindow { get; private set; }

	internal SettingsWindow SettingsWindow { get; private set; }

	internal FirstRunWindow? FirstRunWindow { get; set; }

	internal ServerClient ServerClient { get; private set; }

	internal IDtrBarEntry dtrEntry;

	internal PartyMemberTracker PartyMemberTracker { get; private set; }

	internal Dictionary<string, WTStatus?> PreviousStatus { get; init; } = [];

	private Task? IdyllshirePollTask;
	private CancellationTokenSource? IdyllshireCancelToken;

	#region Life Cycle

	public Plugin(IDalamudPluginInterface pluginInterface) {
		Instance = this;
		pluginInterface.Create<Service>();

		Localization = new(pluginInterface.GetPluginLocDirectory());
		Localization.SetupWithUiCulture();

		Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

		Config.CustomSorts.Clear();
		/*Config.CustomSorts.Add(new() {
			Name = "Min Level ASC",
			Entries = new() {
				new("min-level", false),
				new("name", false)
			}
		});
		Config.CustomSorts.Add(new() {
			Name = "Min Level DESC",
			Entries = new() {
				new("min-level", true),
				new("name", true)
			}
		});*/

		Config.IsIncognito = Config.IncognitoBehavior switch {
			IncognitoBehavior.DisableAtStartup => false,
			IncognitoBehavior.EnableAtStartup => true,
			_ => Config.IsIncognito
		};

		// Client Stuff
		PartyMemberTracker = new();
		ServerClient = new(this);

		// UI
		MainWindow = new(this);
		WindowSystem.AddWindow(MainWindow);

		SettingsWindow = new(this);
		WindowSystem.AddWindow(SettingsWindow);

		// Clickable Duties
		ClickableDuties = new(this);

		// Server Bar
		dtrEntry = Service.DtrBar.Get("WTSync");
		dtrEntry.Shown = false;
		dtrEntry.OnClick = ToggleMain;

		// Commands
		Service.CommandManager.AddHandler("/wtsync", new Dalamud.Game.Command.CommandInfo(OnCommand) {
			HelpMessage = Localization.Localize("cmd.help", "Open the main WTSync window.")
		});

		// Events
		Service.ClientState.Login += OnLogin;
		Service.ClientState.Logout += OnLogout;
		Service.ClientState.TerritoryChanged += OnTerritoryChanged;
		Service.Interface.UiBuilder.Draw += OnDraw;
		Service.Interface.UiBuilder.OpenConfigUi += OnOpenConfig;
		Service.Interface.UiBuilder.OpenMainUi += OnOpenMain;

		Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "WeeklyBingo", OnRefreshBingo);

		// Do we need to accept terms?
		if (!Config.AcceptedTerms) {
			FirstRunWindow = new(this);
			WindowSystem.AddWindow(FirstRunWindow);
			FirstRunWindow.IsOpen = true;

		} else {
			// Update the state and open the window now.
			MainWindow.MaybeOpenAtLoad();
			if (Service.ClientState.IsLoggedIn)
				Service.Framework.RunOnFrameworkThread(() => SendServerUpdate());
		}
	}

	public void OnCommand(string command, string arguments) {
		if ("settings".Equals(arguments, StringComparison.OrdinalIgnoreCase) ||
			"setting".Equals(arguments, StringComparison.OrdinalIgnoreCase)
		)
			OnOpenConfig();
		else
			OnOpenMain();
	}

	public void Dispose() {
		// Remove our data from the server, and wait a bit so the
		// requests actually go through.
		if (ClearServerData())
			ServerClient.WaitForUpload();

		// UI Cleanup
		WindowSystem.RemoveAllWindows();

		MainWindow.Dispose();

		FirstRunWindow = null;

		dtrEntry.Remove();

		// Clickable Duties
		ClickableDuties.Dispose();

		// Commands
		Service.CommandManager.RemoveHandler("/wtsync");

		// Client Cleanup
		ServerClient.Dispose();

		// Event Cleanup
		Service.ClientState.Login -= OnLogin;
		Service.ClientState.Logout -= OnLogout;
		Service.ClientState.TerritoryChanged -= OnTerritoryChanged;
		Service.Interface.UiBuilder.Draw -= OnDraw;
		Service.Interface.UiBuilder.OpenConfigUi -= OnOpenConfig;
		Service.Interface.UiBuilder.OpenMainUi -= OnOpenMain;

		IdyllshireCancelToken?.Cancel();

		try {
			IdyllshireCancelToken?.Dispose();
			IdyllshirePollTask?.Dispose();
		} catch (Exception ex) {
			Service.Logger.Warning($"Unable to dispose of Idyllshire poll task: {ex}");
		}

		IdyllshirePollTask = null;
		IdyllshireCancelToken = null;

		Service.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, "WeeklyBingo", OnRefreshBingo);

		PartyMemberTracker.Dispose();
	}

	public void CloseFirstRunWindow() {
		if (FirstRunWindow != null) {
			WindowSystem.RemoveWindow(FirstRunWindow);
			FirstRunWindow = null;
		}

		if (Config.AcceptedTerms) {
			MainWindow.MaybeOpenAtLoad();
			if (Service.ClientState.IsLoggedIn)
				SendServerUpdate();
		}
	}

	#endregion

	#region Server Communication

	public void ToggleIncognito() {
		Config.IsIncognito = !Config.IsIncognito;
		Config.Save();

		if (Config.IsIncognito) {
			ClearServerData();
			UpdateBar();
		} else
			SendServerUpdate();

		if (MainWindow.IsOpen)
			MainWindow.OnOpen();
	}

	private bool ClearServerData() {
		if (PreviousStatus.Count > 0) {
			ServerClient.HandleLogout(PreviousStatus.Keys);
			PreviousStatus.Clear();
			return true;
		}

		return false;
	}

	public void SendServerUpdate(bool forceUpdate = false) {
		string? myId = GameState.LocalPlayerId;
		if (myId == null || !Service.ClientState.IsLoggedIn)
			return;

		if (!Config.AcceptedTerms)
			return;

		var result = new WTStatusAndId() {
			Id = myId,
			Status = GameState.ReadStatus()
		};

		// Check to see if this changed.
		bool do_update = !Config.IsIncognito && (forceUpdate ||
			!PreviousStatus.TryGetValue(result.Id, out var previous) ||
			!EqualityComparer<WTStatus>.Default.Equals(previous, result.Status));

		// Server Update
		if (do_update) {
			PreviousStatus[result.Id] = result.Status;
			Service.Logger.Debug($"Data changed, or was not already sent this session. Sending.");
			ServerClient.PostUpdate(result.Id, result.Status);
		}

		// And finally, make sure the UI is up to date.
		UpdateBarStatus(result.Status);

		// Bonus!
		if (GameState.IsInIdyllshire)
			MaybeFireIdyllshireTask();
	}

	public (SyncSocketClient?, PartyBingoState)? GetPartyDutyFeed() {
		string? myId = GameState.LocalPlayerId;
		if (myId is null)
			return null;

		var members = Config.IsIncognito ? PartyMemberTracker.LocalOnly : PartyMemberTracker.Members;
		var client = members.Count <= 1 ? null : ServerClient.StartStatusFeed(members
			.Select(x => x.Id)
			// We don't need to subscribe to our own WT state.
			.Where(x => x != myId)
		);

		Dictionary<string, WTStatus> statuses = [];
		var status = GameState.ReadStatus();
		if (status != null)
			statuses[myId] = status;

		return (client, new(members, statuses));
	}

	#endregion

	#region Server Bar

	private WTStatus? BarStatus;

	internal void UpdateBarStatus(WTStatus? status) {
		BarStatus = status;
		UpdateBar();
	}

	internal void UpdateBar() {
		if (BarStatus is null || (Config.HideBarIfNotInDuty && !GameState.IsInDuty))
			dtrEntry.Shown = false;
		else {
			var matchingDuty = GameState.IsInDuty
				? Helpers.GetMatchingEntry(BarStatus)
				: null;

			int claimable = (int) BarStatus.Stickers;
			foreach (var duty in BarStatus.Duties) {
				if (duty.Status == PlayerState.WeeklyBingoTaskStatus.Claimable)
					claimable++;
			}

			if (claimable > 9)
				claimable = 9;

			// Do not show if there's no need to show.
			if (GameState.IsInDuty && Config.HideBarIfNoSticker && (matchingDuty == null || claimable >= 9)) {
				dtrEntry.Shown = false;
				return;
			}

			Vector4? color = null;
			Vector4? colorEdge = null;
			string? icon = null;
			string? extraTip = null;
			string? warnTip = null;

			string tooltip = Localization.Localize("gui.server-bar.tooltip", "Wondrous Tails Completion");
			string label = Localization.Localize("gui.server-bar.info", "WT: {stickers} / 9  {points}")
				.Replace("{stickers}", claimable.ToString())
				.Replace("{points}", BarStatus.SecondChancePoints == 0
					? BarStatus.SecondChancePoints.ToBoxedNumber()
					: BarStatus.SecondChancePoints.ToInstanceNumber());

			if (matchingDuty.HasValue) {
				var duty = matchingDuty.Value.Item1;
				var status = matchingDuty.Value.Item2;

				if (!duty.Text.IsValid)
					extraTip = Localization.Localize("gui.server-bar.tooltip.match", "This duty is in your Wondrous Tails.");
				else
					extraTip = Localization.Localize("gui.server-bar.tooltip.inexact", "This duty is in your Wondrous Tails as \"{name}\".")
						.Replace("{name}", duty.Text.Value.Description.ToString());

				bool isOpen = status == PlayerState.WeeklyBingoTaskStatus.Open;
				bool isClaimable = isOpen || BarStatus.SecondChancePoints > 0;

				if (!isOpen) {
					if (isClaimable)
						warnTip = "\n\n" + Localization.Localize("gui.server-bar.tooltip.claimable", "You have already completed this duty. Use a Second Chance point to retry it if you want to earn another sticker.");
					else
						warnTip = "\n\n" + Localization.Localize("gui.server-bar.tooltip.unavialable", "You have already completed this duty and have no Second Chance points to retry it.");
				}

				icon = isOpen ? "\uE0BE" : "\uE0BF";

				if (Config.BarColorMode > 0) {
					if (isOpen) {
						color = Config.BarColorInDuty;
						colorEdge = Config.BarColorInDutyEdge;
					} else {
						color = Config.BarColorDutyClaimed;
						colorEdge = Config.BarColorDutyClaimedEdge;
					}
				}
			}

			if (Config.IsIncognito) {
				if (string.IsNullOrEmpty(icon))
					icon = "\uE043";

				string tip = Localization.Localize("gui.server-bar.tooltip.incognito", "WTSync is currently offline. Your state is not being shared.");
				if (string.IsNullOrEmpty(extraTip))
					extraTip = tip;
				else
					extraTip = $"{tip}\n\n{extraTip}";
			}

			var builder = new SeStringBuilder();
			if (color.HasValue)
				builder.PushColorRgba(color.Value);
			if (colorEdge.HasValue)
				builder.PushEdgeColorRgba(colorEdge.Value);

			if (!string.IsNullOrEmpty(icon))
				builder
					.Append(icon)
					.Append(' ');

			if (Config.BarColorMode == 1) {
				if (color.HasValue)
					builder.PopColor();
				if (colorEdge.HasValue)
					builder.PopEdgeColor();
			}

			builder.Append(label);

			dtrEntry.Text = builder.ToReadOnlySeString().ToDalamudString();

			builder = new SeStringBuilder();
			builder.Append(tooltip);
			if (!string.IsNullOrEmpty(extraTip) || !string.IsNullOrEmpty(warnTip)) {
				builder.Append("\n\n");
				if (!string.IsNullOrEmpty(extraTip))
					builder
						.AppendSetItalic(true)
						.Append(extraTip)
						.AppendSetItalic(false);

				if (!string.IsNullOrEmpty(warnTip))
					builder
						.AppendSetBold(true)
						.Append(warnTip)
						.AppendSetBold(false);
			}

			dtrEntry.Tooltip = builder.ToReadOnlySeString().ToDalamudString();

			dtrEntry.Shown = true;
		}
	}


	#endregion

	#region Events

	private void MaybeFireIdyllshireTask() {
		IdyllshireCancelToken ??= new();
		IdyllshirePollTask ??= Service.Framework.RunOnTick(() => {
			IdyllshirePollTask = null;
			SendServerUpdate();
		}, new TimeSpan(0, 0, 10), cancellationToken: IdyllshireCancelToken.Token);
	}

	private void OnRefreshBingo(AddonEvent type, AddonArgs args) {
		SendServerUpdate();
	}

	private void OnLogout(int type, int code) {
		ClearServerData();
	}

	private void OnLogin() {
		SendServerUpdate();
	}

	private void OnTerritoryChanged(ushort territoryId) {
		SendServerUpdate();
	}

	public void OnDraw() {
		WindowSystem.Draw();
	}

	public void ToggleMain(AddonMouseEventData eventData) {
		if (!eventData.IsLeftClick)
			return;

		if (MainWindow.IsOpen) {
			if (!Config.OpenWithWT || !CloseWT())
				MainWindow.IsOpen = false;
		} else
			OnOpenMain();
	}

	/// <summary>
	/// If the Wondrous Tails menu is open, close it. Return whether or not we closed it.
	/// </summary>
	public unsafe bool CloseWT() {
		var addon = Service.GameGui.GetAddonByName("WeeklyBingo", 1);
		if (addon.IsNull || !addon.IsVisible)
			return false;

		var cast = (AtkUnitBase*) addon.Address;
		cast->Close(true);
		return true;
	}

	/// <summary>
	/// Check if the Wondrous Tails menu is open, or if it can be opened. Attempt to open it.
	/// Return whether or not it is open (or we tried to open it).
	/// </summary>
	public unsafe bool OpenWT() {
		var addon = Service.GameGui.GetAddonByName("WeeklyBingo", 1);
		if (!addon.IsNull && addon.IsVisible)
			return true;

		// If we are occupied, we can't use an item.
		if (GameState.IsOccupied || GameState.IsDead || GameState.IsCasting)
			return false;

		// First, check if we actually have the item to use.
		var inv = InventoryManager.Instance();
		if (inv is null || inv->GetInventoryItemCount(2002023) < 1)
			return false;

		// Then use it.
		var inst = AgentInventoryContext.Instance();
		if (inst is null)
			return false;

		inst->UseItem(2002023);
		return true;
	}

	/// <summary>
	/// This method checks to see if the user hasn't accepted our terms yet. If
	/// they haven't, then we show them the first run window again and return
	/// true so that whatever method calls this knows it shouldn't display
	/// other UI elements to the user.
	/// </summary>
	public bool MaybeOpenFirstRunInstead() {
		if (Config.AcceptedTerms)
			return false;

		if (FirstRunWindow == null) {
			FirstRunWindow = new(this);
			WindowSystem.AddWindow(FirstRunWindow);
		}

		FirstRunWindow.IsOpen = true;
		FirstRunWindow.BringToFront();

		return true;
	}

	public void OnOpenMain() {
		if (MaybeOpenFirstRunInstead())
			return;

		if (!Config.OpenWithWT || !OpenWT())
			MainWindow.IsOpen = true;
	}

	public void OnOpenConfig() {
		if (MaybeOpenFirstRunInstead())
			return;

		SettingsWindow.OpenSettings();
	}

	#endregion

}
