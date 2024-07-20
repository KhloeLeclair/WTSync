using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Lumina.Excel.GeneratedSheets2;
using Lumina.Text;

using WTSync.Models;

namespace WTSync;

internal static class Helpers {

	internal static string ToInstanceNumber(this int input) {
		if (input < 0) return $"{input}";
		return ToInstanceNumber((uint) input);
	}

	internal static string ToInstanceNumber(this uint input) {
		if (input == 0 || input > 9)
			return $"{input}";

		char converted = Convert.ToChar(0xE0B0 + input);
		return converted.ToString();
	}

	internal static string ToBoxedNumber(this int input) {
		if (input < 0) return $"{input}";
		return ToBoxedNumber((uint) input);
	}

	internal static string ToBoxedNumber(this uint input) {
		if (input > 31)
			return $"{input}";

		char converted = Convert.ToChar(0xE08F + input);
		return converted.ToString();
	}

	internal static string Abbreviate(string input, uint mode) {
		if (mode == 0)
			return input;

		string[] names = input.Split(' ');
		if (names[1].Length > 1 && (mode == 1 || mode == 3))
			names[1] = string.Concat(names[1][..1], ".");
		if (names[0].Length > 1 && (mode == 2 || mode == 3))
			names[0] = string.Concat(names[0][..1], ".");

		return string.Concat(names[0], " ", names[1]);
	}

	internal static string ToTitleCase(this string input) {
		if (input.StartsWith("the "))
			return string.Concat("T", input[1..]);

		return input;

		// While we could do this, we only want to capitalize any initial "The"
		//return CultureInfo.CurrentUICulture.TextInfo.ToTitleCase(input);
	}

	internal static string ToTitleCase(this SeString input) {
		return input.ToString().ToTitleCase();
	}

	internal static string ToTitleCase(this Dalamud.Game.Text.SeStringHandling.SeString input) {
		return input.ToString().ToTitleCase();
	}

	internal static Dictionary<uint, List<ContentFinderCondition>> OrderConditions { get; } = [];

	internal static Dictionary<uint, (byte, byte)> OrderLevelRanges { get; } = [];

	private static List<ContentFinderCondition>? Dungeons { get; set; }
	private static List<ContentFinderCondition>? Alliances { get; set; }
	private static List<ContentFinderCondition>? Raids { get; set; }

	private static Dictionary<uint, ContentFinderCondition>? ConditionByInstance { get; set; }

	[MemberNotNull(nameof(ConditionByInstance))]
	[MemberNotNull(nameof(Dungeons))]
	[MemberNotNull(nameof(Alliances))]
	[MemberNotNull(nameof(Raids))]
	private static void LoadConditionsByInstance() {
		if (ConditionByInstance != null && Dungeons != null && Alliances != null && Raids != null)
			return;

		ConditionByInstance = [];
		Dungeons = [];
		Alliances = [];
		Raids = [];

		var conditionSheet = Service.DataManager.GetExcelSheet<ContentFinderCondition>();
		if (conditionSheet == null)
			return;

		foreach (var cond in conditionSheet) {
			if (cond.ContentType.Row == 2)
				Dungeons.Add(cond);

			if (cond.ContentMemberType.Row == 4)
				Alliances.Add(cond);

			if (cond.ContentMemberType.Row == 3)
				Raids.Add(cond);

			if (cond.ContentLinkType != 1)
				continue;

			ConditionByInstance.TryAdd(cond.Content.Row, cond);
		}
	}

	internal static WeeklyBingoOrderData? GetMatchingEntry(WTStatus status) {
		var sheet = Service.DataManager.GetExcelSheet<WeeklyBingoOrderData>();
		if (sheet == null)
			return null;

		ushort current = Service.ClientState.TerritoryType;

		foreach (var duty in status.Duties) {
			var row = sheet.GetRow(duty.Id);
			if (row == null)
				continue;

			var conds = GetConditionsForEntry(row);
			foreach (var cond in conds) {
				if (cond.TerritoryType.Row == current)
					return row;
			}
		}

		return null;
	}


	internal static List<ContentFinderCondition> GetConditionsForEntry(WeeklyBingoOrderData entry) {

		if (OrderConditions.TryGetValue(entry.RowId, out List<ContentFinderCondition>? conditions))
			return conditions;

		conditions = [];

		var sheet = Service.DataManager.GetExcelSheet<ContentFinderCondition>();
		if (sheet == null)
			return conditions;

		LoadConditionsByInstance();

		switch (entry.Type) {
			case 0:
				// Specific Thing
				if (ConditionByInstance.TryGetValue(entry.Data.Row, out var cond))
					conditions.Add(cond);
				break;

			case 1:
				// Dungeons (exact level)
				foreach (var dungeon in Dungeons) {
					if (dungeon.ClassJobLevelRequired == entry.Data.Row)
						conditions.Add(dungeon);
				}
				break;

			case 2:
				// Dungeons (preceeding levels)
				uint min = entry.Data.Row - 9;
				uint max = entry.Data.Row - 1;
				if (max == 49)
					min = 1;

				foreach (var dungeon in Dungeons) {
					uint lvl = dungeon.ClassJobLevelRequired;
					if (lvl >= min && lvl <= max)
						conditions.Add(dungeon);
				}
				break;

			case 3:
				// Special Instances
				switch (entry.Data.Row) {
					case 5: // Crystaline Conflict
						break;

					case 6: // Frontline
						foreach (var thing in sheet) {
							if (thing.ContentType.Row == 6 && thing.ContentMemberType.Row == 7)
								conditions.Add(thing);
						}
						break;

					case 9: // Deep Dungeons
						foreach (var thing in sheet) {
							if (thing.ContentType.Row == 21)
								conditions.Add(thing);
						}
						break;

					case 10: // Treasure Dungeons
						foreach (var thing in sheet) {
							if (thing.ContentType.Row == 9)
								conditions.Add(thing);
						}
						break;

					case 12: // Rival Wings
						foreach (var thing in sheet) {
							if (thing.ContentType.Row == 6 && thing.ContentMemberType.Row == 18)
								conditions.Add(thing);
						}
						break;
				}
				break;

			case 4:
				// Raid Sets
				uint[]? ids = null;
				uint? allianceLevel = null;

				switch (entry.Data.Row) {
					case 2: // Binding Coil
						ids = [93, 94, 95, 96, 97];
						break;

					case 3: // Second Coil
							// Does savage count?
						ids = [98, 99, 100, 101];
						break;

					case 4: // Final Coil
						ids = [107, 108, 109, 110];
						break;

					case 5: // Alexander Gordias
						ids = [112, 113, 114, 115];
						break;

					case 6: // Alexander Midas
						ids = [136, 137, 138, 139];
						break;

					case 7: // Alexander Creator
						ids = [186, 187, 188, 189];
						break;

					case 8: // Omega Deltascape
						ids = [252, 253, 254, 255];
						break;

					case 9: // Omega Sigmascape
						ids = [286, 287, 288, 289];
						break;

					case 10: // Omega Alphascape
						ids = [587, 588, 589, 590];
						break;

					case 11: // Eden Gate Resurrection or Descent
						ids = [653, 684];
						break;

					case 12: // Eden Gate Inundation or Sepulture
						ids = [682, 689];
						break;

					case 13: // Eden Verse Fulmination or Furor
						ids = [715, 719];
						break;

					case 14: // Eden Verse Iconoclasm or Refulgence
						ids = [726, 728];
						break;

					case 15: // Eden Promise Umbra or Litany
						ids = [747, 749];
						break;

					case 16: // Eden Promise Anamorphosis or Eternity
						ids = [751, 758];
						break;

					case 17: // Asphodelos First or Second
						ids = [808, 810];
						break;

					case 18: // Asphodelos Third or Fourth
						ids = [800, 806];
						break;

					case 19: // Abyssos Fifth or Sixth
						ids = [880, 872];
						break;

					case 20: // Abyssos Seventh or Eighth
						ids = [876, 883];
						break;

					case 21: // Anabaseios: Ninth or Tenth
						ids = [936, 938];
						break;

					case 22: // Anabaseios: Eleventh or Twelfth
						ids = [940, 942];
						break;

					case 23: // Eden's Gate
						ids = [653, 682, 684, 689];
						break;

					case 24: // Eden's Verse
						ids = [715, 719, 726, 728];
						break;

					case 25: // Eden's Promise
						ids = [747, 749, 751, 758];
						break;

					case 26: // Alliance (ARR)
						allianceLevel = 50;
						break;

					case 27: // Alliance (HW)
						allianceLevel = 60;
						break;

					case 28: // Alliance (SB)
						allianceLevel = 70;
						break;

					case 29: // Alliance (ShB)
						allianceLevel = 80;
						break;

					case 30: // Alliance (EW)
						allianceLevel = 90;
						break;

					default:
						ids = null;
						break;
				}

				if (allianceLevel.HasValue)
					foreach (var raid in Alliances) {
						uint lvl = raid.ClassJobLevelRequired;
						if (lvl >= allianceLevel.Value && lvl <= allianceLevel.Value)
							conditions.Add(raid);
					}

				if (ids != null)
					foreach (uint id in ids) {
						var thing = sheet.GetRow(id);
						if (thing != null)
							conditions.Add(thing);
					}

				break;
		}

		byte minLevel = byte.MaxValue;
		byte maxLevel = byte.MinValue;

		foreach (var cond in conditions) {
			byte lvl = cond.ContentType.Row == 9 ? cond.ClassJobLevelSync : cond.ClassJobLevelRequired;
			if (lvl > maxLevel)
				maxLevel = lvl;
			if (lvl < minLevel)
				minLevel = lvl;
		}

		conditions.Sort((a, b) => {
			if (a.ClassJobLevelRequired != b.ClassJobLevelRequired)
				return a.ClassJobLevelRequired.CompareTo(b.ClassJobLevelRequired);

			return a.SortKey.CompareTo(b.SortKey);
		});

		OrderConditions[entry.RowId] = conditions;
		OrderLevelRanges[entry.RowId] = (minLevel, maxLevel);
		return conditions;
	}


}
