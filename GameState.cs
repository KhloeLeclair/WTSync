using System;
using System.Collections.Generic;

using Dalamud.Game.ClientState.Conditions;

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

	internal static List<PartyMember> ReadPartyMembers() {
		if (!Service.ClientState.IsLoggedIn)
			return [];

		return ReadCWParty() ?? ReadNormalParty();
	}

	internal static int GetPartyCount() {
		if (!Service.ClientState.IsLoggedIn)
			return 0;

		return ReadCWPartyCount() ?? ReadNormalPartyCount();
	}

	private static int ReadNormalPartyCount() {
		int result = Service.PartyList.Count;
		return result == 0 ? 1 : result;
	}

	private static List<PartyMember> ReadNormalParty() {
		List<PartyMember> result = [];

		if (Service.PartyList.Count == 0) {
			if (Service.ClientState.LocalPlayer != null && Service.ClientState.LocalContentId > 0)
				result.Add(new(Service.ClientState.LocalPlayer.Name.ToString(), (ulong) Service.ClientState.LocalContentId));
			return result;
		}

		for (int i = 0; i < Service.PartyList.Count; i++) {
			var member = Service.PartyList[i];
			if (member is not null && member.ContentId > 0)
				result.Add(new(member.Name.ToString(), (ulong) member.ContentId));
		}

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
				result.Add(new(member.NameString, member.ContentId));
		}

		return result;
	}

	internal static unsafe WTStatus? ReadStatus() {
		if (!Service.ClientState.IsLoggedIn)
			return null;

		var inst = PlayerState.Instance();
		if (inst is null || !inst->HasWeeklyBingoJournal || inst->IsWeeklyBingoExpired())
			return new WTStatus() {
				Expires = DateTime.MinValue,
				Stickers = 0,
				SecondChancePoints = inst is null ? 0 : inst->WeeklyBingoNumSecondChancePoints,
				Duties = []
			};

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
