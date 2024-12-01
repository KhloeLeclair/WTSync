using System;

using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

using Lumina.Excel.Sheets;

namespace WTSync;

internal class ClickableDuties : IDisposable {

	private readonly Plugin Plugin;
	private Configuration Config => Plugin.Config;

	private IAddonEventHandle?[]? EventHandles;

	private int LastClicked = -1;
	private int LastDutyIndex = -1;

	internal unsafe ClickableDuties(Plugin plugin) {
		Plugin = plugin;

		Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnPostSetup);

		if (Config.ClickableDuties) {
			var addon = (AddonWeeklyBingo*) Service.GameGui.GetAddonByName("WeeklyBingo");
			if (addon is not null && addon->IsReady)
				SetupEvents(addon);
		}
	}

	public unsafe void UpdateSetting() {
		if (!Config.ClickableDuties) {
			RemoveEvents();

		} else if (EventHandles is null) {
			var addon = (AddonWeeklyBingo*) Service.GameGui.GetAddonByName("WeeklyBingo");
			if (addon is not null && addon->IsReady)
				SetupEvents(addon);
		}
	}

	public void Dispose() {
		Service.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnPostSetup);
		LastClicked = -1;

		RemoveEvents();
	}

	private void RemoveEvents() {
		if (EventHandles is null)
			return;

		foreach (var handle in EventHandles)
			if (handle != null)
				Service.AddonEventManager.RemoveEvent(handle);

		EventHandles = null;
	}

	private unsafe void SetupEvents(AddonWeeklyBingo* addon) {
		RemoveEvents();

		EventHandles = new IAddonEventHandle[16];
		for (int i = 0; i < 16; i++) {
			var slot = addon->DutySlotList[i];
			EventHandles[i] = Service.AddonEventManager.AddEvent(
				(nint) addon,
				(nint) slot.DutyButton->OwnerNode,
				AddonEventType.ButtonClick,
				OnClickDutySlot
			);
		}
	}


	private unsafe void OnPostSetup(AddonEvent type, AddonArgs args) {
		LastClicked = -1;
		if (Config.ClickableDuties)
			SetupEvents((AddonWeeklyBingo*) args.Addon);
	}

	private unsafe void OnClickDutySlot(AddonEventType atkEventType, nint atkUnitBase, nint atkResNode) {
		var node = (AtkResNode*) atkResNode;
		var inst = PlayerState.Instance();
		if (inst is null)
			return;

		// The node IDs for the duty buttons start with 12.
		int idx = (int) node->NodeId - 12;
		if (idx < 0 || idx >= 16)
			return;

		bool wasLast = idx == LastClicked;
		LastClicked = idx;

		// Only add behavior for open tasks.

		// While we could also handle claimed, then we'd need to detect if
		// the person is using second chance points to retry something and
		// frankly I don't care that much.
		var state = inst->GetWeeklyBingoTaskStatus(idx);
		if (state != PlayerState.WeeklyBingoTaskStatus.Open)
			return;

		// Alright, we did click a thing. Get the info on it.
		var nullableData = Service.DataManager.GetExcelSheet<WeeklyBingoOrderData>()?.GetRowOrDefault(inst->WeeklyBingoOrderData[idx]);
		if (nullableData is not WeeklyBingoOrderData data)
			return;

		if (data.Type == 3 && data.Data.RowId == 6)
			// Frontlines
			GameState.OpenRoulette(7);

		else if (data.Type == 3 && data.Data.RowId == 5)
			// Crystalline Conflict
			GameState.OpenRoulette(40);

		else {
			var conditions = Helpers.GetConditionsForEntry(data);
			if (Config.RandomDutyOnClick && conditions.Count > 1) {
				// Random Duty
				int old = LastDutyIndex;
				int i = 0;
				while (i++ < 5 && LastDutyIndex == old)
					LastDutyIndex = Random.Shared.Next(0, conditions.Count);

				GameState.OpenDutyFinder(conditions[LastDutyIndex]);

			} else if (conditions.Count > 0) {
				LastDutyIndex = ((wasLast ? LastDutyIndex : -1) + 1) % conditions.Count;
				GameState.OpenDutyFinder(conditions[LastDutyIndex]);
			}
		}

	}

}
