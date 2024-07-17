using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

using Lumina.Excel.GeneratedSheets2;

namespace WTSync.Models;


public class PartyBingoState {

	public static readonly uint[] LEVEL_GROUPS = [50, 60, 70, 80, 90, 100];

	/// <summary>
	/// A map of player IDs to their names.
	/// </summary>
	public Dictionary<ulong, string> PlayerNames { get; }

	/// <summary>
	/// A map of player IDs to their WTStatus entries.
	/// </summary>
	public Dictionary<ulong, WTStatus> Statuses { get; private set; }

	/// <summary>
	/// A map of player IDs to the number of stickers they have earned (including claimable duties).
	/// </summary>
	public Dictionary<ulong, uint> Stickers { get; private set; }

	/// <summary>
	/// A list of BingoEntry instances for every bingo entry of
	/// all the players in the party.
	/// </summary>
	public List<BingoEntry> Entries { get; private set; }

	/// <summary>
	/// A list of content types of all the matching content for all the
	/// entries. This can be used to render filters.
	/// </summary>
	public List<ContentType> ContentTypes { get; private set; }

	/// <summary>
	/// A list of BingoEntry instances for display. This is filtered from
	/// <see cref="Entries"/>, applying sorting and any content type and
	/// content level filters.
	/// </summary>
	public List<BingoEntry> DisplayEntries {
		get {
			if (_CachedDisplayEntries == null)
				UpdateDisplayEntries();

			return _CachedDisplayEntries;
		}
	}

	private List<BingoEntry>? _CachedDisplayEntries;

	#region Filter Data

	// TODO: Persist filter data.

	private static bool[] _LevelFilters = new bool[LEVEL_GROUPS.Length];

	public bool[] LevelFilters {
		get => _LevelFilters;
		set {
			_LevelFilters = value;
			_CachedDisplayEntries = null;
		}
	}

	private static Dictionary<uint, bool> _TypeFilters = [];

	public Dictionary<uint, bool> TypeFilters {
		get => _TypeFilters;
		set {
			_TypeFilters = value;
			_CachedDisplayEntries = null;
		}
	}

	#endregion

	public PartyBingoState(List<PartyMember> members, IEnumerable<KeyValuePair<ulong, WTStatus>>? statuses) {
		// First, load the party names.
		PlayerNames = [];
		foreach (var member in members)
			PlayerNames[member.Id] = member.Name;

		// Now, load the statuses if we have them.
		UpdateStatuses(statuses ?? []);
	}

	[MemberNotNull(nameof(Statuses))]
	[MemberNotNull(nameof(Stickers))]
	[MemberNotNull(nameof(Entries))]
	[MemberNotNull(nameof(ContentTypes))]
	public void UpdateStatuses(IEnumerable<KeyValuePair<ulong, WTStatus>> statuses) {
		Statuses = new(statuses);

		UpdateStickers();
		UpdateEntries();
	}


	#region Calculation

	[MemberNotNull(nameof(Stickers))]
	private void UpdateStickers() {
		Stickers = [];

		foreach (var entry in Statuses) {
			uint count = entry.Value.Stickers;
			foreach (var duty in entry.Value.Duties) {
				if (duty.Status == PlayerState.WeeklyBingoTaskStatus.Claimable)
					count++;
			}

			Stickers[entry.Key] = count;
		}
	}

	[MemberNotNull(nameof(Entries))]
	[MemberNotNull(nameof(ContentTypes))]
	private void UpdateEntries() {
		Entries = [];
		ContentTypes = [];

		// Make sure we don't have cached display entries.
		_CachedDisplayEntries = null;

		// Also make sure we have the data sheet.
		var sheet = Service.DataManager.GetExcelSheet<WeeklyBingoOrderData>();
		if (sheet == null)
			return;

		// First, collect all the duties, and which players have them.
		Dictionary<uint, Dictionary<ulong, PlayerState.WeeklyBingoTaskStatus>> Orders = [];

		foreach (var entry in Statuses) {
			foreach (var duty in entry.Value.Duties) {
				if (!Orders.TryGetValue(duty.Id, out var orders)) {
					orders = [];
					Orders[duty.Id] = orders;
				}

				orders[entry.Key] = duty.Status;
			}
		}

		// Now, create the actual entries.
		foreach (var entry in Orders) {
			var row = sheet.GetRow(entry.Key);
			if (row == null)
				continue;

			var thing = new BingoEntry(entry.Key, row, entry.Value, PlayerNames);
			Entries.Add(thing);

			foreach (var type in thing.ContentTypes)
				if (!ContentTypes.Contains(type))
					ContentTypes.Add(type);
		}

		ContentTypes.Sort((a, b) => {
			return a.RowId.CompareTo(b.RowId);
			//return a.Name.ToString().CompareTo(b.Name.ToString());
		});

	}

	public void UpdateFilters() {
		_CachedDisplayEntries = null;
	}

	[MemberNotNull(nameof(_CachedDisplayEntries))]
	private void UpdateDisplayEntries() {

		Service.Logger.Debug($"Filtering and sorting entries.");

		int typesToggled = 0;
		foreach (bool entry in _TypeFilters.Values) {
			if (entry)
				typesToggled++;
		}

		int levelsToggled = 0;
		foreach (bool entry in _LevelFilters) {
			if (entry)
				levelsToggled++;
		}

		bool filterTypes = typesToggled != 0 && typesToggled != ContentTypes.Count;
		bool filterLevels = levelsToggled != 0 && levelsToggled != LEVEL_GROUPS.Length;

		if (!filterLevels && !filterTypes) {
			_CachedDisplayEntries = new(Entries);

		} else {
			_CachedDisplayEntries = [];

			foreach (var entry in Entries) {
				if (filterTypes) {
					bool matched = false;
					foreach (var type in entry.ContentTypes) {
						if (_TypeFilters.GetValueOrDefault(type.RowId)) {
							matched = true;
							break;
						}
					}
					if (!matched)
						continue;
				}

				if (filterLevels) {
					bool matched = false;
					uint last = 0;
					for (int i = 0; i < LEVEL_GROUPS.Length; i++) {
						uint lvl = LEVEL_GROUPS[i];
						last++;

						uint minLevel = entry.MinLevel;
						uint maxLevel = entry.MaxLevel;

						if (_LevelFilters[i] && ((minLevel >= last && minLevel <= lvl) || (maxLevel >= last && maxLevel <= lvl))) {
							matched = true;
							break;
						}

						last = lvl;
					}

					if (!matched)
						continue;
				}

				_CachedDisplayEntries.Add(entry);
			}
		}

		// TODO: Customizable sorting.
		_CachedDisplayEntries.Sort((a, b) => {
			int aPlayers = a.PlayersOpen.Count;
			int bPlayers = b.PlayersOpen.Count;

			// First, sort by the number of players with the duty open (descending)
			if (aPlayers != bPlayers)
				return bPlayers.CompareTo(aPlayers);

			// Next, sort by minimum level (ascending)
			if (a.MinLevel != b.MinLevel)
				return a.MinLevel.CompareTo(b.MinLevel);

			// Third, sort by total number of players (descending)
			aPlayers += a.PlayersClaimable.Count + a.PlayersClaimed.Count;
			bPlayers += b.PlayersClaimable.Count + b.PlayersClaimed.Count;

			if (aPlayers != bPlayers)
				return bPlayers.CompareTo(aPlayers);

			// Finally, sort by name (ascending)
			return a.DisplayName.CompareTo(b.DisplayName);
		});

	}

	#endregion

}
