// Ignore Spelling: Plugin

using System.Collections.Generic;
using System.Linq;

using Dalamud;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

using Lumina.Excel.GeneratedSheets2;

using WTSync.Models;
using WTSync.UI;

namespace WTSync;

public sealed class Plugin : IDalamudPlugin {

	internal static Plugin Instance { get; private set; } = null!;

	internal Localization Localization { get; init; }

	internal Configuration Config { get; private set; }

	internal WindowSystem WindowSystem { get; } = new WindowSystem("WTSync");

	internal MainWindow MainWindow { get; private set; }

	internal SettingsWindow SettingsWindow { get; private set; }

	internal FirstRunWindow? FirstRunWindow { get; set; }

	internal ServerClient ServerClient { get; private set; }

	internal IDtrBarEntry dtrEntry;

	internal PartyMemberTracker PartyMemberTracker { get; private set; }

	internal Dictionary<string, WTStatus?> PreviousStatus { get; init; } = [];

	#region Life Cycle

	public Plugin(IDalamudPluginInterface pluginInterface) {
		Instance = this;
		pluginInterface.Create<Service>();

		Localization = new(pluginInterface.GetPluginLocDirectory());
		Localization.SetupWithUiCulture();

		Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

		// Client Stuff
		PartyMemberTracker = new();
		ServerClient = new(this);

		// UI
		MainWindow = new(this);
		WindowSystem.AddWindow(MainWindow);

		SettingsWindow = new(this);
		WindowSystem.AddWindow(SettingsWindow);

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
				SendServerUpdate();
		}
	}

	public void OnCommand(string command, string arguments) {
		if ("settings".Equals(arguments, System.StringComparison.OrdinalIgnoreCase) ||
			"setting".Equals(arguments, System.StringComparison.OrdinalIgnoreCase)
		)
			OnOpenConfig();
		else
			OnOpenMain();
	}

	public void Dispose() {
		// UI Cleanup
		WindowSystem.RemoveAllWindows();

		MainWindow.Dispose();

		FirstRunWindow = null;

		dtrEntry.Remove();

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

	public void SendServerUpdate() {
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
		bool do_update = !PreviousStatus.TryGetValue(result.Id, out var previous) ||
			!EqualityComparer<WTStatus>.Default.Equals(previous, result.Status);

		// Server Update
		if (do_update) {
			PreviousStatus[result.Id] = result.Status;
			Service.Logger.Debug($"Data changed, or was not already sent this session. Sending.");
			ServerClient.PostUpdate(result.Id, result.Status);
		}

		// And finally, make sure the UI is up to date.
		UpdateBarStatus(result.Status);
	}

	public (SyncSocketClient?, PartyBingoState)? GetPartyDutyFeed() {
		string? myId = GameState.LocalPlayerId;
		if (myId is null)
			return null;

		var members = PartyMemberTracker.Members;
		var client = members.Count == 1 ? null : ServerClient.StartStatusFeed(members
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
		if (BarStatus is null)
			dtrEntry.Shown = false;
		else {
			WeeklyBingoOrderData? matchingDuty = GameState.IsInDuty
				? Helpers.GetMatchingEntry(BarStatus)
				: null;

			int claimable = (int) BarStatus.Stickers;
			foreach (var duty in BarStatus.Duties) {
				if (duty.Status == PlayerState.WeeklyBingoTaskStatus.Claimable)
					claimable++;
			}

			if (claimable > 9)
				claimable = 9;

			dtrEntry.Tooltip = Localization.Localize("gui.server-bar.tooltip", "Wondrous Tails Completion");
			dtrEntry.Text = Localization.Localize("gui.server-bar.info", "WT: {stickers} / 9  {points}")
				.Replace("{stickers}", claimable.ToString())
				.Replace("{points}", BarStatus.SecondChancePoints.ToInstanceNumber());

			if (matchingDuty != null) {
				string extraTip;
				if (matchingDuty.Text.Row == 0 || matchingDuty.Text.Value is null)
					extraTip = Localization.Localize("gui.server-bar.tooltip.match", "This duty is in your Wondrous Tails.");
				else
					extraTip = Localization.Localize("gui.server-bar.tooltip.inexact", "This duty is in your Wondrous Tails as \"{name}\".")
						.Replace("{name}", matchingDuty.Text.Value.Description.ToString());

				dtrEntry.Tooltip += "\n\n" + extraTip;
				dtrEntry.Text = $"\uE0BE {dtrEntry.Text}";
			}

			dtrEntry.Shown = true;
		}
	}


	#endregion

	#region Events

	private void OnRefreshBingo(AddonEvent type, AddonArgs args) {
		SendServerUpdate();
	}

	private void OnLogout() {
		ServerClient.HandleLogout(PreviousStatus.Keys);
		PreviousStatus.Clear();
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

	public void ToggleMain() {
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
		var addon = (AtkUnitBase*) Service.GameGui.GetAddonByName("WeeklyBingo", 1);
		if (addon is null || !addon->IsVisible)
			return false;

		addon->Close(true);
		return true;
	}

	/// <summary>
	/// Check if the Wondrous Tails menu is open, or if it can be opened. Attempt to open it.
	/// Return whether or not it is open (or we tried to open it).
	/// </summary>
	public unsafe bool OpenWT() {
		var addon = (AtkUnitBase*) Service.GameGui.GetAddonByName("WeeklyBingo", 1);
		if (addon is not null && addon->IsVisible)
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
