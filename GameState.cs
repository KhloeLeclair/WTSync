using System;
using System.Collections.Generic;

using Dalamud.Game.ClientState.Conditions;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

using Lumina.Excel.GeneratedSheets2;

using WTSync.Models;

namespace WTSync;

internal static class GameState {

	internal static bool IsInQueueOrDuty => IsInQueue || IsInDuty;

	internal static bool IsInIdyllshire => Service.ClientState.TerritoryType == 478;

	internal static bool IsInQueue {
		get {
			if (!Service.ClientState.IsLoggedIn)
				return false;

			return Service.Condition.Any(
				ConditionFlag.InDutyQueue,
				ConditionFlag.WaitingForDuty,
				ConditionFlag.WaitingForDutyFinder
			);
		}
	}

	internal static bool IsDead {
		get {
			if (!Service.ClientState.IsLoggedIn || Service.ClientState.LocalPlayer == null)
				return false;

			return Service.ClientState.LocalPlayer.IsDead || Service.Condition.Any(ConditionFlag.Unconscious);
		}
	}

	internal static bool IsCasting {
		get {
			if (!Service.ClientState.IsLoggedIn || Service.ClientState.LocalPlayer == null)
				return false;

			return Service.ClientState.LocalPlayer.IsCasting || Service.Condition.Any(
				ConditionFlag.Casting,
				ConditionFlag.Casting87
			);
		}
	}

	internal static bool IsOccupied {
		get {
			if (!Service.ClientState.IsLoggedIn)
				return false;

			return Service.Condition.Any(
				ConditionFlag.BetweenAreas,
				ConditionFlag.BetweenAreas51,
				ConditionFlag.Mounted2, // Riding someone else's mount
				ConditionFlag.Occupied,
				ConditionFlag.Occupied30,
				ConditionFlag.Occupied33,
				ConditionFlag.Occupied38,
				ConditionFlag.Occupied39,
				ConditionFlag.OccupiedInCutSceneEvent,
				ConditionFlag.OccupiedInEvent,
				ConditionFlag.OccupiedInQuestEvent,
				ConditionFlag.OccupiedSummoningBell,
				ConditionFlag.InThatPosition
			);
		}
	}

	internal static bool IsInDuty {
		get {
			if (!Service.ClientState.IsLoggedIn)
				return false;

			return Service.Condition.Any(
				ConditionFlag.BoundByDuty,
				ConditionFlag.BoundByDuty56,
				ConditionFlag.BoundByDuty95
			);
		}
	}

	private static string? _LocalPlayerId;
	private static ulong _LocalContentId = 0;

	internal static string? LocalPlayerId {
		get {
			if (_LocalContentId != Service.ClientState.LocalContentId || _LocalPlayerId == null) {
				_LocalContentId = Service.ClientState.LocalContentId;
				var player = Service.ClientState.LocalPlayer;
				if (player == null)
					_LocalPlayerId = null;
				else
					_LocalPlayerId = $"{player.Name}@{player.HomeWorld.Id}".ToSha256();
			}

			return _LocalPlayerId;
		}
	}

	internal static List<PartyMember> ReadPartyMembers() {
		if (!Service.ClientState.IsLoggedIn)
			return [];

		return ReadCWParty() ?? ReadNormalGroup();
	}

	internal static int GetPartyCount() {
		if (!Service.ClientState.IsLoggedIn)
			return 0;

		return ReadCWPartyCount() ?? ReadNormalPartyCount();
	}

	private static unsafe int ReadNormalPartyCount() {
		var inst = FFXIVClientStructs.FFXIV.Client.Game.Group.GroupManager.Instance();
		int amount = 0;
		if (inst is not null) {
			var group = inst->GetGroup();
			if (!group->IsAlliance && !group->IsSmallGroupAlliance)
				amount = group->MemberCount;
		}

		return amount == 0 ? 1 : amount;
	}

	private static unsafe List<PartyMember> ReadNormalGroup() {
		List<PartyMember> result = [];

		var inst = FFXIVClientStructs.FFXIV.Client.Game.Group.GroupManager.Instance();
		if (inst is not null) {
			var group = inst->GetGroup();
			if (!group->IsAlliance && !group->IsSmallGroupAlliance)
				for (int i = 0; i < group->MemberCount; i++) {
					var member = group->PartyMembers[i];
					if (member.ContentId > 0)
						result.Add(new(member.NameString, member.ToId()));
				}
		}

		if (result.Count == 0 && Service.ClientState.LocalPlayer != null && Service.ClientState.LocalContentId > 0)
			result.Add(new(Service.ClientState.LocalPlayer.Name.ToString(), GameState.LocalPlayerId!));

		return result;
	}

	private static unsafe int? ReadCWPartyCount() {
		var inst = InfoProxyCrossRealm.Instance();
		// If we aren't in a cross-world party, or if we're in an alliance, then
		// return null. This will force a fall-back 
		if (inst is null || inst->IsInAllianceRaid != 0 || inst->IsInCrossRealmParty == 0)
			return null;

		// Sanity checking.
		byte idx = inst->LocalPlayerGroupIndex;
		if (idx < 0 || idx >= inst->GroupCount)
			return null;

		var group = inst->CrossRealmGroups[idx];
		return group.GroupMemberCount;
	}

	private static unsafe List<PartyMember>? ReadCWParty() {
		var inst = InfoProxyCrossRealm.Instance();
		// If we aren't in a cross-world party, or if we're in an alliance, then
		// return null. This will force a fall-back 
		if (inst is null || inst->IsInAllianceRaid != 0 || inst->IsInCrossRealmParty == 0)
			return null;

		// Sanity checking.
		byte idx = inst->LocalPlayerGroupIndex;
		if (idx < 0 || idx >= inst->GroupCount)
			return null;

		List<PartyMember> result = [];

		var group = inst->CrossRealmGroups[idx];
		for (int i = 0; i < group.GroupMemberCount; i++) {
			var member = group.GroupMembers[i];
			if (member.ContentId > 0)
				result.Add(new(member.NameString, member.ToId()));
		}

		return result;
	}

	internal static unsafe WTStatus? ReadStatus() {
		if (!Service.ClientState.IsLoggedIn)
			return null;

		// Check to see if the player has the actual journal item.
		var inv = InventoryManager.Instance();
		bool hasJournal = !(inv is null || inv->GetInventoryItemCount(2002023) < 1);

		var inst = PlayerState.Instance();
		if (!hasJournal || inst is null || !inst->HasWeeklyBingoJournal || inst->IsWeeklyBingoExpired())
			return null; /* new WTStatus() {
				Expires = DateTime.MinValue,
				Stickers = 0,
				SecondChancePoints = inst is null ? 0 : inst->WeeklyBingoNumSecondChancePoints,
				Duties = []
			};*/

		WTDutyStatus[] Duties = new WTDutyStatus[16];

		for (int i = 0; i < 16; i++) {
			byte orderId = inst->WeeklyBingoOrderData[i];
			var status = inst->GetWeeklyBingoTaskStatus(i);

			Duties[i] = new() {
				Id = orderId,
				Status = status,
			};
		}

		return new WTStatus() {
			Expires = inst->WeeklyBingoExpireDateTime,
			Stickers = (uint) Math.Max(0, inst->WeeklyBingoNumPlacedStickers),
			SecondChancePoints = inst->WeeklyBingoNumSecondChancePoints,
			Duties = Duties
		};
	}


	internal static unsafe void OpenDutyFinder(ContentFinderCondition condition) {
		var inst = AgentContentsFinder.Instance();
		if (inst is null || GameState.IsInDuty) return;

		// Don't open DF for treasure maps / deep dungeons
		if (condition.ContentType.Row == 9 || condition.ContentType.Row == 21)
			return;

		if (!GameState.IsInQueue) {
			// TODO: Manage the unrestricted party setting.
		}

		inst->OpenRegularDuty(condition.RowId);
	}

	internal static unsafe void OpenRoulette(byte id) {
		var inst = AgentContentsFinder.Instance();
		if (inst is null || GameState.IsInQueue) return;

		inst->OpenRouletteDuty(id);
	}

}
