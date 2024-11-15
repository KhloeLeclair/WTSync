using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using Dalamud.Game.ClientState.Party;

using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;

using WTSync.Models;

namespace WTSync;

internal static class Helpers {

	public static readonly Vector4 BLACK = new Vector4(0, 0, 0, 1f);
	public static readonly Vector4 BAR_GREEN = new Vector4(0x00, 0xCC / 255f, 0x22 / 255f, 0xFF / 255f);
	public static readonly Vector4 BAR_ORANGE = new Vector4(0xFF / 255f, 0x7B / 255f, 0x1A / 255f, 1f);

	private static bool HasLoggedAVE;

	internal static string? ToSha256(this string input) {
		if (string.IsNullOrEmpty(input))
			return string.Empty;

		try {
			byte[] buffer = Encoding.UTF8.GetBytes(input);
			byte[] digest = SHA256.HashData(buffer);
			return Convert.ToHexString(digest);

		} catch(AccessViolationException ex) {
			if (!HasLoggedAVE) {
				Service.Logger.Error($"There was an error while trying to hash a string. Details:\n{ex}");
				HasLoggedAVE = true;
			}

			return null;
		}
	}

	internal static string? ToId(this FFXIVClientStructs.FFXIV.Client.Game.Group.PartyMember member) {
		return $"{member.NameString}@{member.HomeWorld}".ToSha256();
	}

	internal static string? ToId(this IPartyMember member) {
		return $"{member.Name}@{member.World.RowId}".ToSha256();
	}

	internal static string? ToId(this CrossRealmMember member) {
		return $"{member.NameString}@{member.HomeWorld}".ToSha256();
	}

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

	internal static string ToTitleCase(this ReadOnlySeString input) {
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
	private static List<ContentFinderCondition>? Trials { get; set; }

	private static Dictionary<uint, ContentFinderCondition>? ConditionByInstance { get; set; }

	[MemberNotNull(nameof(ConditionByInstance))]
	[MemberNotNull(nameof(Dungeons))]
	[MemberNotNull(nameof(Alliances))]
	[MemberNotNull(nameof(Raids))]
	[MemberNotNull(nameof(Trials))]
	private static void LoadConditionsByInstance() {
		if (ConditionByInstance != null && Dungeons != null && Alliances != null && Raids != null && Trials != null)
			return;

		ConditionByInstance = [];
		Dungeons = [];
		Alliances = [];
		Raids = [];
		Trials = [];

		var conditionSheet = Service.DataManager.GetExcelSheet<ContentFinderCondition>();
		if (conditionSheet == null)
			return;

		foreach (var cond in conditionSheet) {
			if (cond.ContentType.RowId == 2)
				Dungeons.Add(cond);

			//if (cond.ContentType.RowId == 5) {
				if (cond.AllianceRoulette)
					Alliances.Add(cond);
				else if (cond.NormalRaidRoulette)
					// TODO: Check for Binding of Coil?
					Raids.Add(cond);
			//}

			//if (cond.ContentType.RowId == 4 && cond.ContentMemberType.RowId == 3) {
				if (cond.TrialRoulette)
					Trials.Add(cond);
			//}

			if (cond.ContentLinkType != 1)
				continue;

			ConditionByInstance.TryAdd(cond.Content.RowId, cond);
		}
	}

	internal static (WeeklyBingoOrderData, PlayerState.WeeklyBingoTaskStatus)? GetMatchingEntry(WTStatus status) {
		var sheet = Service.DataManager.GetExcelSheet<WeeklyBingoOrderData>();
		if (sheet == null)
			return null;

		ushort current = Service.ClientState.TerritoryType;

		foreach (var duty in status.Duties) {
			if (!sheet.TryGetRow(duty.Id, out var row))
				continue;

			var conds = GetConditionsForEntry(row);
			foreach (var cond in conds) {
				if (cond.TerritoryType.RowId == current)
					return (row, duty.Status);
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
		uint min;
		uint max;

		switch (entry.Type) {
			case 0:
				// Specific Thing
				if (ConditionByInstance.TryGetValue(entry.Data.RowId, out var cond))
					conditions.Add(cond);
				break;

			case 1:
				// Dungeons (exact level)
				foreach (var dungeon in Dungeons) {
					if (dungeon.ClassJobLevelRequired == entry.Data.RowId)
						conditions.Add(dungeon);
				}
				break;

			case 2:
				// Dungeons (preceeding levels)
				min = entry.Data.RowId - 8;
				max = entry.Data.RowId - 0;
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
				switch (entry.Data.RowId) {
					case 5: // Crystaline Conflict
						foreach (var thing in sheet) {
							if (thing.ContentType.RowId == 6 && (thing.ContentMemberType.RowId == 29 || thing.ContentMemberType.RowId == 30))
								conditions.Add(thing);
						}
						break;

					case 6: // Frontline
						foreach (var thing in sheet) {
							if (thing.ContentType.RowId == 6 && thing.ContentMemberType.RowId == 7)
								conditions.Add(thing);
						}
						break;

					case 9: // Deep Dungeons
						foreach (var thing in sheet) {
							if (thing.ContentType.RowId == 21)
								conditions.Add(thing);
						}
						break;

					case 10: // Treasure Dungeons
						foreach (var thing in sheet) {
							if (thing.ContentType.RowId == 9)
								conditions.Add(thing);
						}
						break;

					case 12: // Rival Wings
						foreach (var thing in sheet) {
							if (thing.ContentType.RowId == 6 && thing.ContentMemberType.RowId == 18)
								conditions.Add(thing);
						}
						break;
				}
				break;

			case 4:
				// Raid Sets
				uint[]? ids = null;
				uint? allianceLevel = null;

				switch (entry.Data.RowId) {
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

					case 31: // Asphodelos 1-4
						ids = [808, 810, 800, 806];
						break;

					case 32: // Abyssos 5-8
						ids = [880, 872, 876, 883];
						break;

					case 33: // Anabaseios 9-12
						ids = [936, 938, 940, 942];
						break;

					case 34: // AAC Light-heavyweight M1 - M2
						ids = [985, 987];
						break;

					case 35: // AAC Light-heavyweight M3 - M4
						ids = [989, 991];
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
						if (sheet.TryGetRow(id, out var row))
							conditions.Add(row);
					}

				break;

			case 5:
				// Leveling Dungeons (preceeding levels)
				max = entry.Data.RowId;
				min = max switch {
					49 => 1, // Lv. 1-49
					79 => 51, // Lv. 51-79
					99 => 81, // Lv. 81-99,
					_ => max // Unknown
				};

				foreach (var dungeon in Dungeons) {
					uint lvl = dungeon.ClassJobLevelRequired;
					// Any dungeon in the level range in leveling roulette.
					if (lvl >= min && lvl <= max && dungeon.LevelingRoulette)
						conditions.Add(dungeon);
				}
				break;

			case 6:
				// High-Level Dungeons
				max = entry.Data.RowId;
				min = max switch {
					60 => 50, // Lv. 50-60
					80 => 70, // Lv. 70-80
					90 => 90, // Lv. 90
					_ => max, // Unknown
				};

				foreach (var dungeon in Dungeons) {
					uint lvl = dungeon.ClassJobLevelRequired;
					// Any dungeon within that level range in the high-level roulette.
					if (lvl >= min && lvl <= max && (dungeon.HighLevelRoulette || dungeon.ExpertRoulette))
						conditions.Add(dungeon);
				}
				break;

			case 7:
				// Trials
				max = entry.Data.RowId;
				min = max switch {
					60 => 50,  // Lv. 50-60
					100 => 70, // Lv. 70-100
					_ => max,  // Unknown
				};

				foreach(var trial in Trials) {
					uint lvl = trial.ClassJobLevelRequired;
					// Any trial within that level range
					if (lvl >= min && lvl <= max)
						conditions.Add(trial);
				}
				break;

			case 8:
				// Alliances
				max = entry.Data.RowId;
				min = max switch {
					60 => 50, // Lv. 50-60
					90 => 70, // Lv. 70-90
					_ => max, // Unknown
				};

				foreach(var alliance in Alliances) {
					uint lvl = alliance.ClassJobLevelRequired;
					if (lvl >= min && lvl <= max)
						conditions.Add(alliance);
				}

				break;

			case 9:
				// Raids
				max = entry.Data.RowId;
				min = max switch {
					60 => 50, // Lv. 50-60
					100 => 70, // Lv. 70-100,
					_ => max, // Unknown
				};

				foreach(var raid in Raids) {
					uint lvl = raid.ClassJobLevelRequired;
					if (lvl >= min && lvl <= max)
						conditions.Add(raid);
				}

				break;
		}

		byte minLevel = byte.MaxValue;
		byte maxLevel = byte.MinValue;

		foreach (var cond in conditions) {
			byte lvl = cond.ContentType.RowId == 9 ? cond.ClassJobLevelSync : cond.ClassJobLevelRequired;
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
