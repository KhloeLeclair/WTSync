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

		// Cancel any ongoing cancellation.
		CancelDestroyStateTask();

		// If we have existing state, check to see if the party is
		// still the same.
		if (PartyState != null) {
			var members = Plugin.PartyMemberTracker.Members;
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
	}


	public override void Draw() {
		bool got_click = false;

		UpdateFlags();

		var style = ImGui.GetStyle();

		bool isSolo = PartyState != null && PartyState.PlayerNames.Count <= 1;

		if (isSolo) {
			ImGui.TextColored(ImGuiColors.DalamudGrey, Localization.Localize("gui.load-state.offline", "Offline"));
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(Localization.Localize("gui.load-state.offline.about", "You are not in a party, so there is no need to connect to the server to receive data."));

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

		ImGui.SameLine(rightSide - btnWidth - btnWidth - style.ItemSpacing.X);

		bool isUpdating = Plugin.ServerClient.HasPendingUpdate;
		string? updateError = Plugin.ServerClient.LastError;
		bool isError = updateError is not null;

		ImGui.PushID("sync-button");

		if (ImGuiComponents.IconButton(isError
			? FontAwesomeIcon.ExclamationTriangle
			: isUpdating
				? FontAwesomeIcon.Sync
				: FontAwesomeIcon.Upload
			) && !isUpdating
		)
			Plugin.SendServerUpdate(true);

		if (ImGui.IsItemHovered()) {
			string tip = isUpdating
				? Localization.Localize("gui.force-sync.updating", "Your status is being uploaded to the server. Please wait a few seconds.")
				: Localization.Localize("gui.force-sync.about", "Click this button to re-submit your status to the server.\n\nYou should only need to use this if your party members\ndon't see your Wondrous Tails data correctly for some reason.");

			if (Plugin.ServerClient.LastError != null)
				tip += "\n\n" + Localization.Localize("gui.force-sync.error", "There was an error uploading your status to the server.\nWe will try again.\n\nError:") + "\n" + Plugin.ServerClient.LastError;

			ImGui.SetTooltip(tip);
		}

		ImGui.PopID();

		ImGui.SameLine(rightSide - btnWidth);

		ImGui.PushID("opensettings");
		if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
			Plugin.OnOpenConfig();
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.settings", "WTSync Settings"));
		ImGui.PopID();

		if (PartyState == null) {
			if (ImGui.IsAnyMouseDown() && !got_click)
				LastClickedThing = 0;

			return;
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
					bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
					if (rightClicked || ImGui.IsItemClicked()) {
						// Click Tracking
						bool wasLastClicked = LastClickedThing == entry.Id;
						LastClickedThing = (int) entry.Id;
						got_click = true;

						if (entry.Data.Type == 3 && entry.Data.Data.Row == 6)
							GameState.OpenRoulette(7); // Frontline

						else if (entry.Data.Type == 3 && entry.Data.Data.Row == 5)
							GameState.OpenRoulette(40); // Crystalline Conflict

						else if (entry.Conditions.Count > 0) {
							int direction = rightClicked ? -1 : 1;
							ClickIndex = wasLastClicked ? ClickIndex + direction : 0;
							if (ClickIndex < 0)
								ClickIndex = entry.Conditions.Count - 1;
							else
								ClickIndex %= entry.Conditions.Count;

							GameState.OpenDutyFinder(entry.Conditions[ClickIndex]);
						}
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

			// Level Label
			var labelSize = ImGui.CalcTextSize(label);

			ImGui.SetCursorPos(new Vector2(startX + labelSize.X + spaceSize.X, pos.Y));

			if (entry.MinLevel == entry.MaxLevel)
				label = $"Lv. {entry.MinLevel}";
			else
				label = $"Lv. {entry.MinLevel}-{entry.MaxLevel}";

			ImGui.TextColored(Config.ColorLevelLabel, label);


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
}
