// Ignore Spelling: plugin

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Dalamud;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;

using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

using ImGuiNET;

using WTSync.Models;

namespace WTSync.UI;

internal class MainWindow : Window, IDisposable {

	public const ImGuiWindowFlags NORMAL_FLAGS = ImGuiWindowFlags.None;
	public const ImGuiWindowFlags OPEN_FLAGS = ImGuiWindowFlags.NoFocusOnAppearing;
	public const ImGuiWindowFlags ATTACHED_FLAGS = ImGuiWindowFlags.NoMove |
		ImGuiWindowFlags.NoDocking |
		ImGuiWindowFlags.NoFocusOnAppearing;

	private readonly Plugin Plugin;

	private Configuration Config => Plugin.Config;

	private Vector2? LastSize;

	public PartyBingoState? PartyState;

	private int LastClickedThing;
	private int ClickIndex;

	private SyncSocketClient? SyncSocketClient;

	private CancellationTokenSource? destroyStateCancel;
	private Task? destroyStateTask;

	private Task<ShareResult>? ShareTask;
	private ShareResult? ShareResult;

	private DateTime LastPing = DateTime.MinValue;

	public MainWindow(Plugin plugin) : base(
		"WTSync",
		NORMAL_FLAGS
	) {
		Plugin = plugin;
		UpdateFlags();

		Size = new(400, 100);
		SizeCondition = ImGuiCond.FirstUseEver;

		SizeConstraints = new() {
			MinimumSize = new(350, 200)
		};

		Plugin.PartyMemberTracker.MembersChanged += OnMembersChanged;

		Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnPostSetup);
		Service.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "WeeklyBingo", OnPreDraw);
		Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "WeeklyBingo", OnPreFinalize);
	}

	public void Dispose() {
		CancelDestroyStateTask();
		SyncSocketClient?.Dispose();
		SyncSocketClient = null;
		PartyState = null;

		ShareTask?.Dispose();
		ShareTask = null;
		ShareResult = null;

		Plugin.PartyMemberTracker.MembersChanged -= OnMembersChanged;

		Service.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnPostSetup);
		Service.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, "WeeklyBingo", OnPreDraw);
		Service.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "WeeklyBingo", OnPreFinalize);
	}

	internal unsafe void MaybeOpenAtLoad() {
		if (!Config.OpenWithWT || !Config.AcceptedTerms)
			return;

		var addon = (AtkUnitBase*) Service.GameGui.GetAddonByName("WeeklyBingo", 1);
		if (addon is null || !addon->IsVisible)
			return;

		IsOpen = true;
	}

	private void OnPostSetup(AddonEvent type, AddonArgs args) {
		if (!Config.AcceptedTerms)
			return;

		if (Config.OpenWithWT)
			IsOpen = true;
	}

	private unsafe void OnPreDraw(AddonEvent type, AddonArgs args) {
		int side = Config.AttachToWT;
		if (side == 0 || !IsOpen || ImGui.IsAnyMouseDown())
			return;

		var addon = (AtkUnitBase*) args.Addon;
		if (addon is null || !addon->IsVisible)
			return;

		float x = addon->X;

		if (side == 1)
			x += addon->GetScaledWidth(true) - (addon->Scale * 100);
		else if (LastSize.HasValue)
			x -= LastSize.Value.X - (addon->Scale * 40);

		Position = new(x, addon->Y);
		PositionCondition = ImGuiCond.Always;

		// TODO: Check how many stickers the player has and send an update.
	}

	private void OnPreFinalize(AddonEvent type, AddonArgs args) {
		if (Config.OpenWithWT)
			IsOpen = false;

		PositionCondition = ImGuiCond.FirstUseEver;
	}


	public unsafe void UpdateFlags() {

		var addon = (AtkUnitBase*) Service.GameGui.GetAddonByName("WeeklyBingo", 1);
		bool isWTOpen = addon is not null && addon->IsVisible;

		if (!isWTOpen) {
			ShowCloseButton = true;
			Flags = NORMAL_FLAGS;

		} else {
			ShowCloseButton = !Config.OpenWithWT;

			if (!Config.OpenWithWT)
				Flags = NORMAL_FLAGS;

			else if (Config.AttachToWT == 0) {
				Flags = OPEN_FLAGS;
				PositionCondition = ImGuiCond.FirstUseEver;

			} else {
				Flags = ATTACHED_FLAGS;
			}
		}

	}

	public override void Update() {
		base.Update();

		if (ShareTask != null) {
			if (ShareTask.IsCompleted) {
				ShareResult = ShareTask.Result;
				ShareTask = null;
			}
		}

		if (SyncSocketClient != null) {
			if (PartyState == null) {
				SyncSocketClient.Dispose();
				SyncSocketClient = null;
				return;
			}

			var now = DateTime.UtcNow;
			if (now - LastPing > TimeSpan.FromSeconds(5)) {
				LastPing = now;
				SyncSocketClient.SendStatusRequest();
			}

			if (SyncSocketClient.HasUpdate) {
				Dictionary<string, WTStatus> statuses = new(PartyState.Statuses);

				while (true) {
					if (!SyncSocketClient.TryGetUpdate(out var update))
						break;

					if (update.Status == null)
						statuses.Remove(update.Id);
					else
						statuses[update.Id] = update.Status;
				}

				PartyState.UpdateStatuses(statuses);
			}
		}
	}

	#region State Management

	private void CancelDestroyStateTask() {
		destroyStateCancel?.Cancel();
		destroyStateCancel?.Dispose();
		destroyStateCancel = null;

		if (destroyStateTask != null && destroyStateTask.IsCompleted)
			destroyStateTask.Dispose();

		destroyStateTask = null;
	}

	private void StartDestroyStateTask() {
		if (destroyStateTask != null)
			return;

		destroyStateCancel = new();
		destroyStateTask = Task.Delay(10000).ContinueWith(t => {
			if (!t.IsCanceled) {
				CancelDestroyStateTask();
				SyncSocketClient?.Dispose();
				SyncSocketClient = null;
				PartyState = null;
			}
		}, destroyStateCancel.Token);
	}

	private void OnMembersChanged(object? sender, List<PartyMember> e) {
		// Basically, re-run our OnOpen event if the window is open.
		if (IsOpen)
			OnOpen();
	}

	#endregion


	public override void OnClose() {
		base.OnClose();

		// Start destroying the state after a wait.
		StartDestroyStateTask();
	}


	public override void OnOpen() {
		base.OnOpen();

		if (ShareTask != null) {
			try {
				ShareTask.Dispose();
			} catch (Exception ex) {
				Service.Logger.Warning($"Error disposing of share task: {ex}");
			}
			ShareTask = null;
		}

		ShareResult = null;

		// Cancel any ongoing cancellation.
		CancelDestroyStateTask();

		// If we have existing state, check to see if the party is
		// still the same.
		if (PartyState != null) {
			var members = Config.IsIncognito ? Plugin.PartyMemberTracker.LocalOnly : Plugin.PartyMemberTracker.Members;
			var oldMembers = PartyState.PlayerNames.Keys;

			if (oldMembers.Count == members.Count) {
				bool matched = true;
				foreach (var member in members) {
					if (!oldMembers.Contains(member.Id)) {
						matched = false;
						break;
					}
				}
				if (matched)
					return;
			}

			// Got here, we need to clean old state.
			SyncSocketClient?.Dispose();
			SyncSocketClient = null;
			PartyState = null;
		}

		// If we got here, we need new party state.
		var data = Plugin.GetPartyDutyFeed();
		if (data != null)
			(SyncSocketClient, PartyState) = data.Value;

		UpdateSorter();
	}

	public void UpdateSorter() {
		if (PartyState == null)
			return;

		if (Config.LastSort >= 0 && Config.LastSort < Config.CustomSorts.Count)
			PartyState.Sorter = EntrySorter.BuildSorter(Config.CustomSorts[Config.LastSort].Entries);
		else
			PartyState.Sorter = EntrySorter.DefaultComparison;
	}


	public override void Draw() {
		bool got_click = false;

		UpdateFlags();

		var style = ImGui.GetStyle();

		bool isSolo = PartyState != null && PartyState.PlayerNames.Count <= 1;
		bool isBusted = PartyState != null && PartyState.PlayerNames.Count <= 0;
		bool isUpdating = Plugin.ServerClient.HasPendingUpdate;
		string? updateError = Plugin.ServerClient.LastError;
		bool isError = updateError is not null;

		ImGui.PushID("incognito-toggle");

		//if (!Config.IsIncognito)
		//	ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.HealerGreen);

		if (ImGuiComponents.IconButton(
			Config.IsIncognito
				? FontAwesomeIcon.Globe
				: isError
					? FontAwesomeIcon.ExclamationTriangle
					: FontAwesomeIcon.PowerOff
		))
			Plugin.ToggleIncognito();

		//if (!Config.IsIncognito)
		//	ImGui.PopStyleColor();

		if (ImGui.IsItemHovered()) {
			string? tip;
			if (Config.IsIncognito)
				tip = Localization.Localize("gui.incognito.offline", "You are offline. Your state is not being shared.");
			else if (isUpdating)
				tip = Localization.Localize("gui.connection.updating", "Your status is being uploaded to the server. Please wait a few seconds.");
			else if (Plugin.ServerClient.LastError != null)
				tip = Localization.Localize("gui.connection.error", "There was an error uploading your status to the server.\nWe will try again.\n\nDetails can be found in Dalamud's log.");
			else
				tip = null;

			string to_do;
			if (Config.IsIncognito)
				to_do = Localization.Localize("gui.incognito.to-disable", "Click here to go back online and start sharing your Wondrous Tails again.");
			else
				to_do = Localization.Localize("gui.incognito.to-enable", "Click to go offline and stop sharing your Wondrous Tails.");

			if (tip != null)
				ImGui.SetTooltip(to_do + "\n\n" + tip);
			else
				ImGui.SetTooltip(to_do);
		}

		ImGui.PopID();

		ImGui.SameLine();

		if (Config.IsIncognito) {
			ImGui.TextColored(ImGuiColors.DalamudGrey, Localization.Localize("gui.load-state.incognito", "Offline"));
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(isSolo
					? Localization.Localize("gui.load-state.incognito.about-solo", "You are using WTSync in offline mode.")
					: Localization.Localize("gui.load-state.incognito.about", "You are using WTSync in offline mode. Go back online to check your party's data."));

		} else if (isSolo) {
			ImGui.TextColored(ImGuiColors.DalamudGrey, Localization.Localize("gui.load-state.solo", "Solo"));
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(Localization.Localize("gui.load-state.solo.about", "You are not in a party, so there is no need to connect to the server to receive data."));

		} else if (SyncSocketClient == null) {
			ImGui.Text(Localization.Localize("gui.load-state.disconnected", "Disconnected."));
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(Localization.Localize("gui.load-state.disconnected.about", "You are not currently connected to the server."));

		} else if (!SyncSocketClient.IsConnected) {
			ImGui.Text(Localization.Localize("gui.load-state.connecting", "Connecting..."));
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(Localization.Localize("gui.load-state.connecting.about", "A connection to the server is being established."));

		} else {
			ImGui.Text(Localization.Localize("gui.load-state.connected", "Connected."));
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(
					Localization.Localize(
						"gui.load-state.connected.about",
						"You are connected to the server to receive party data.\nThere are currently {client} clients connected."
					)
					.Replace("{client}", $"{SyncSocketClient.ConnectedClients}")
				);
		}

		float rightSide = ImGui.GetWindowContentRegionMax().X;
		float btnWidth = ImGui.GetFrameHeight();

		if (!Config.IsIncognito && ShareTask == null && ShareResult == null) {
			ImGui.SameLine(rightSide - btnWidth - btnWidth - style.ItemSpacing.X);

			ImGui.PushID("share-button");
			if (ImGuiComponents.IconButton(FontAwesomeIcon.ShareAlt)) {
				ShareResult = null;
				ShareTask = Task.Run(Plugin.ServerClient.MakeShareLink);
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(Localization.Localize("gui.share.about", "Share your party's WTSync status!\n\nThis generates a link that someone without WTSync can use to view this information in their browser."));

			ImGui.PopID();
		}

		ImGui.SameLine(rightSide - btnWidth);

		ImGui.PushID("opensettings");
		if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
			Plugin.OnOpenConfig();
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.settings", "WTSync Settings"));
		ImGui.PopID();

		if (PartyState == null) {
			ImGui.Spacing();
			ImGui.Spacing();
			ImGui.Spacing();

			ImGui.TextWrapped(Localization.Localize("gui.no-state", "Somehow, we don't know what state your party is in.\n\nSomething probably went wrong."));

			if (ImGui.IsAnyMouseDown() && !got_click)
				LastClickedThing = 0;

			return;
		}

		// If we have a share task...
		if (ShareTask != null)
			ImGui.TextWrapped(Localization.Localize("gui.share.loading", "Generating share link..."));
		else if (ShareResult is ShareResult sr) {
			if (!string.IsNullOrEmpty(sr.Error)) {
				ImGui.TextColored(ImGuiColors.DalamudYellow, Localization.Localize("gui.share.error", "Share Error:"));
				ImGui.TextWrapped(sr.Error);
			} else {
				ImGui.TextWrapped(Localization.Localize("gui.share.success", "Here's your link! This will expire in 30 minutes."));
				ImGui.Spacing();

				string url = sr.Url ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(url)) {
					ImGui.InputText(Localization.Localize("gui.url", "URL"), ref url, (uint) url.Length, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.ReadOnly);
					ImGui.SameLine();
					ImGui.PushID("copy-to-clipboard#url");
					if (ImGuiComponents.IconButton(FontAwesomeIcon.Clipboard)) {
						ImGui.SetClipboardText(url);
						string msg = Localization.Localize("gui.copied-to-clipboard", "Copied to Clipboard");
						Service.NotificationManager.AddNotification(new() {
							MinimizedText = msg,
							Content = msg,
							Type = Dalamud.Interface.ImGuiNotification.NotificationType.Success
						});
					}
					if (ImGui.IsItemHovered())
						ImGui.SetTooltip(Localization.Localize("gui.copy-to-clipboard", "Copy to Clipboard"));
					ImGui.PopID();

					if (ImGui.Button(Localization.Localize("gui.open-browser", "Open in Browser")))
						Helpers.TryOpenURL(url);
				}
			}

			if (ImGui.Button(Localization.Localize("gui.done", "Done")))
				ShareResult = null;

			var pos = ImGui.GetCursorPos();
			ImGui.SetCursorPosY(pos.Y + 32f);
		}

		// First, show each person in the party and their stickers.
		int cols = 2;
		if (Config.ShowSCP)
			cols++;
		if (Config.ShowExpiration)
			cols++;

		ImGui.BeginTable("MemberTable", cols, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);

		ImGui.TableSetupColumn(Localization.Localize("gui.name", "Name"));
		ImGui.TableSetupColumn(Localization.Localize("gui.stickers", "Stickers"));
		if (Config.ShowSCP)
			ImGui.TableSetupColumn(Localization.Localize("gui.points", "Second Chance"));
		if (Config.ShowExpiration)
			ImGui.TableSetupColumn(Localization.Localize("gui.expires", "Expires"));

		if (!Service.GameConfig.UiControl.TryGetUInt("PartyListNameType", out uint nameType))
			nameType = 0;

		ImGui.TableHeadersRow();

		foreach (var entry in PartyState.PlayerNames) {
			string name = Helpers.Abbreviate(entry.Value, nameType);
			bool state = PartyState.PlayerFilters.Contains(entry.Key);

			ImGui.TableNextRow();

			ImGui.TableNextColumn();

			if (state)
				//if (entry.Key == Service.ClientState.LocalContentId)
				ImGui.TextColored(Config.ColorButtonActive, name);
			else
				ImGui.Text(name);

			if (ImGui.IsItemClicked()) {
				if (state)
					PartyState.PlayerFilters.Remove(entry.Key);
				else
					PartyState.PlayerFilters.Add(entry.Key);
				PartyState.UpdateFilters();
			}

			if (PartyState.Statuses.TryGetValue(entry.Key, out var status)) {
				uint stickers = PartyState.Stickers.GetValueOrDefault(entry.Key);

				ImGui.TableNextColumn();
				string text = $"{stickers} / 9";
				if (stickers == 9)
					ImGui.TextColored(Config.ColorMaxStickers, text);
				else
					ImGui.Text(text);

				if (Config.ShowSCP) {
					uint points = status.SecondChancePoints;
					ImGui.TableNextColumn();
					if (points == 9)
						ImGui.TextColored(Config.ColorMaxSecondChancePoints, points.ToString());
					else
						ImGui.Text(points.ToString());
				}

				if (Config.ShowExpiration) {
					ImGui.TableNextColumn();
					ImGui.Text(status.Expires.ToShortDateString());
				}

			} else {
				ImGui.TableNextColumn();
				ImGui.TextColored(ImGuiColors.DalamudGrey, "---");
				if (Config.ShowSCP) {
					ImGui.TableNextColumn();
					ImGui.TextColored(ImGuiColors.DalamudGrey, "---");
				}
				if (Config.ShowExpiration) {
					ImGui.TableNextColumn();
					ImGui.TextColored(ImGuiColors.DalamudGrey, "---");
				}
			}
		}

		ImGui.EndTable();

		// Content Type Filtering

		ImGui.Text(Localization.Localize("gui.filter.content-type", "Type:"));

		ImGui.PushStyleColor(ImGuiCol.ButtonActive, Config.ColorButtonActive);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudGrey3);
		ImGui.PushStyleColor(ImGuiCol.Button, 0);

		foreach (var type in PartyState.ContentTypes) {
			bool state = PartyState.TypeFilters.GetValueOrDefault(type.RowId);

			ImGui.SameLine();

			if (state) {
				ImGui.PushStyleColor(ImGuiCol.Button, Config.ColorButtonActive);
				ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Config.ColorButtonActive);
			}

			// Left-click behavior: toggle entry
			// Right-click behavior: toggle entry exclusively

			var img = Service.TextureProvider.GetFromGameIcon(new() { IconId = type.IconDutyFinder, HiRes = true }).GetWrapOrEmpty();
			if (ImGui.ImageButton(img.ImGuiHandle, new Vector2(img.Width, img.Height))) {
				PartyState.TypeFilters[type.RowId] = !state;
				PartyState.UpdateFilters();
			}

			if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
				PartyState.TypeFilters.Clear();
				PartyState.TypeFilters[type.RowId] = !state;
				PartyState.UpdateFilters();
			}

			if (state) {
				ImGui.PopStyleColor();
				ImGui.PopStyleColor();
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(type.Name.ToString());
		}

		ImGui.PopStyleColor();
		ImGui.PopStyleColor();
		ImGui.PopStyleColor();

		ImGui.SameLine(rightSide - btnWidth);
		ImGui.PushID($"filternoopen");

		bool filterNo = PartyState.FilterNoOpen;
		if (filterNo) {
			ImGui.PushStyleColor(ImGuiCol.Button, Config.ColorButtonActive);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Config.ColorButtonActive);
		}

		if (ImGuiComponents.IconButton(filterNo ? FontAwesomeIcon.FilterCircleXmark : FontAwesomeIcon.Filter)) {
			PartyState.FilterNoOpen = !filterNo;
		}

		if (filterNo) {
			ImGui.PopStyleColor();
			ImGui.PopStyleColor();
		}

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.filter.no-players", "Do not show duties that no players have open."));

		ImGui.PopID();

		// Level Range Filtering

		ImGui.Text(Localization.Localize("gui.filter.level", "Level:"));

		ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudGrey3);

		int lvlsToggled = 0;
		uint last = 0;
		for (int i = 0; i < PartyBingoState.LEVEL_GROUPS.Length; i++) {
			uint lvl = PartyBingoState.LEVEL_GROUPS[i];
			last++;
			bool state = PartyState.LevelFilters[i];
			if (state)
				lvlsToggled++;

			ImGui.SameLine();

			if (state) {
				ImGui.PushStyleColor(ImGuiCol.Button, Config.ColorButtonActive);
				ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Config.ColorButtonActive);
			}

			// Left-click behavior: toggle entry
			// Right-click behavior (cycles):
			//   1. enable this + all lower
			//   2. enable this + all higher
			//   3. disable all

			if (ImGui.Button($"{lvl}")) {
				PartyState.LevelFilters[i] = !state;
				PartyState.UpdateFilters();
			}

			if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
				bool wasLastButton = LastClickedThing < 0;
				bool wasLastClicked = wasLastButton && LastClickedThing == -lvl;
				LastClickedThing = -(int) lvl;
				ClickIndex = wasLastClicked
					? (ClickIndex + 1) % 3
					: (wasLastButton && ClickIndex != 2)
						? ClickIndex
						: 0;

				got_click = true;

				switch (ClickIndex) {
					case 0:
					default:
						// [--this]
						for (int j = 0; j < PartyState.LevelFilters.Length; j++)
							PartyState.LevelFilters[j] = j <= i;

						break;

					case 1:
						// [this--]
						for (int j = 0; j < PartyState.LevelFilters.Length; j++)
							PartyState.LevelFilters[j] = j >= i;

						break;

					case 2:
						// none
						Array.Fill(PartyState.LevelFilters, false);
						break;
				}

				PartyState.UpdateFilters();
			}

			if (state) {
				ImGui.PopStyleColor();
				ImGui.PopStyleColor();
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip($"Lv. {last}-{lvl}");

			last = lvl;
		}

		ImGui.PopStyleColor();

		// Sorting

		if (Config.CustomSorts.Count > 0) {
			var sortMethod = Config.LastSort >= 0 && Config.LastSort < Config.CustomSorts.Count
				? Config.CustomSorts[Config.LastSort] : null;

			string currentName = CustomSort.GetName(sortMethod);

			if (ImGui.BeginCombo(Localization.Localize("gui.sort", "Sort"), currentName)) {

				bool selected = Config.LastSort == -1;

				if (ImGui.Selectable(Localization.Localize("sort.default", "Default"), selected)) {
					Config.LastSort = -1;
					Config.Save();
					UpdateSorter();
				}

				if (selected)
					ImGui.SetItemDefaultFocus();

				for (int i = 0; i < Config.CustomSorts.Count; i++) {
					var item = Config.CustomSorts[i];
					selected = Config.LastSort == i;

					if (ImGui.Selectable(item.GetName(), selected)) {
						Config.LastSort = i;
						Config.Save();
						UpdateSorter();
					}

					if (selected)
						ImGui.SetItemDefaultFocus();
				}

				ImGui.EndCombo();
			}
		}


		// Rendering the List

		// TODO: Re-factor to support multiple columns

		var windowSize = ImGui.GetContentRegionMax();
		var spaceSize = ImGui.CalcTextSize(" ");

		string dividerText = ", ";
		var dividerSize = ImGui.CalcTextSize(dividerText);

		// TOOD: Figure out how to do a virtual list to reduce the impact or
		// drawing a bunch of images that aren't within the visible scroll area.

		foreach (var entry in PartyState.DisplayEntries) {

			var pos = ImGui.GetCursorPos();

			// First, the image for the entry.
			int width;
			int height;

			bool rightClicked;

			if (Plugin.Config.ImageScale == 0) {
				width = 0;
				height = 0;
			} else {
				var imgWrap = entry.Icon.GetWrapOrEmpty();
				width = (int) (imgWrap.Width * Plugin.Config.ImageScale);
				height = (int) (imgWrap.Height * Plugin.Config.ImageScale);

				if (width > 0 && height > 0) {
					ImGui.Image(imgWrap.ImGuiHandle, new Vector2(width, height));

					// Behavior: If the user clicks / right-clicks the image, we should
					// open the Duty Finder for them.
					rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
					if (rightClicked || ImGui.IsItemClicked()) {
						OnClickEntry(entry, rightClicked);
						got_click = true;
					}

					// Behavior: Display a list of matching duties when hovering over the image.
					if (ImGui.IsItemHovered()) {
						string? tip = entry.ToolTip;
						if (!string.IsNullOrEmpty(tip))
							ImGui.SetTooltip(tip);
					}
				}
			}

			// Now that we've drawn the image, store the right-side + padding position so we
			// can use it as necessary. Also store the maximum Y position.
			float startX = pos.X + width + (width > 0 ? style.WindowPadding.X : 0);
			float maxY = pos.Y + height;

			// Next, we need to draw the title of this entry.
			ImGui.SetCursorPos(new Vector2(startX, pos.Y));

			string label = entry.DisplayName;
			ImGui.Text(label);

			// Entry click reactivity (continued)
			rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
			if (rightClicked || ImGui.IsItemClicked()) {
				OnClickEntry(entry, rightClicked);
				got_click = true;
			}

			// Level Label
			var labelSize = ImGui.CalcTextSize(label);

			ImGui.SetCursorPos(new Vector2(startX + labelSize.X + spaceSize.X, pos.Y));

			if (entry.MinLevel == entry.MaxLevel)
				label = $"Lv. {entry.MinLevel}";
			else
				label = $"Lv. {entry.MinLevel}-{entry.MaxLevel}";

			ImGui.TextColored(Config.ColorLevelLabel, label);

			// Entry click reactivity (continued)
			rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
			if (rightClicked || ImGui.IsItemClicked()) {
				OnClickEntry(entry, rightClicked);
				got_click = true;
			}


			// Player entries.

			// First, we need to start tracking positions so we can do wrapping.
			float xPos = startX;
			float yPos = pos.Y + labelSize.Y + 2; // Each line gets slight padding.
			float lineHeight = 0;

			bool firstOnLine = true;

			// Now, draw them.
			foreach (var member in entry.Players) {

				// Format the name for display.
				string name = Helpers.Abbreviate(member.Item2, nameType);
				var nameSize = ImGui.CalcTextSize(name);

				// Check to see if we have enough space, and wrap if we don't.
				if (!firstOnLine && xPos + dividerSize.X + nameSize.X > windowSize.X) {
					firstOnLine = true;
					xPos = startX;
					yPos += lineHeight + 2;
					lineHeight = 0;
				}

				// If this isn't the first item, add our divider.
				if (!firstOnLine) {
					ImGui.SetCursorPos(new Vector2(xPos, yPos));
					ImGui.TextColored(ImGuiColors.DalamudGrey, dividerText);
					xPos += dividerSize.X;
					lineHeight = Math.Max(lineHeight, dividerSize.Y);
				}

				firstOnLine = false;

				// Now, draw the name.
				ImGui.SetCursorPos(new(xPos, yPos));
				xPos += nameSize.X;
				lineHeight = Math.Max(lineHeight, nameSize.Y);

				bool isOpen = member.Item3 == PlayerState.WeeklyBingoTaskStatus.Open;

				ImGui.TextColored(isOpen ? Config.ColorDutyAvailable : ImGuiColors.DalamudGrey, name);
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(
						(isOpen
							? Localization.Localize("gui.claimable.open", "{name} can complete this duty to earn a sticker.")
							: Localization.Localize("gui.claimable.closed", "{name} has already completed this duty.")
						).Replace("{name}", name)
					);
			}

			if (xPos != startX)
				yPos += lineHeight;

			if (yPos > maxY)
				maxY = yPos;

			// Prepare for Next Entry
			ImGui.SetCursorPos(new Vector2(pos.X, maxY + style.WindowPadding.Y));
		}

		// Finally, update the click tracker and the last window size.

		LastSize = ImGui.GetWindowSize();
		if (LastClickedThing != 0 && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) && !got_click) {
			LastClickedThing = 0;
		}
	}

	void OnClickEntry(BingoEntry entry, bool rightClicked = false) {
		// Click Tracking
		bool wasLastClicked = LastClickedThing == entry.Id;
		LastClickedThing = (int) entry.Id;

		if (entry.Data.Type == 3 && entry.Data.Data.RowId == 6)
			// Frontlines
			GameState.OpenRoulette(7);

		else if (entry.Data.Type == 3 && entry.Data.Data.RowId == 5)
			// Crystalline Conflict
			GameState.OpenRoulette(40);

		else if (Config.RandomDutyOnClick && entry.Conditions.Count > 1) {
			// Random Duty
			int old = ClickIndex;
			int i = 0;
			while (i++ < 5 && ClickIndex == old)
				ClickIndex = Random.Shared.Next(0, entry.Conditions.Count);

			GameState.OpenDutyFinder(entry.Conditions[ClickIndex]);

		} else if (entry.Conditions.Count > 0) {
			// Cycle Duties
			int direction = rightClicked ? -1 : 1;
			ClickIndex = wasLastClicked ? ClickIndex + direction : 0;
			if (ClickIndex < 0)
				ClickIndex = entry.Conditions.Count - 1;
			else
				ClickIndex %= entry.Conditions.Count;

			GameState.OpenDutyFinder(entry.Conditions[ClickIndex]);
		}
	}

}
