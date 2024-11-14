using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

using Lumina.Excel.Sheets;

namespace WTSync.Models;


public class PartyBingoState {

	public static readonly uint[] LEVEL_GROUPS = [50, 60, 70, 80, 90, 100];

	/// <summary>
	/// A map of player IDs to their names.
	/// </summary>
	public Dictionary<string, string> PlayerNames { get; }

	/// <summary>
	/// A map of player IDs to their WTStatus entries.
	/// </summary>
	public Dictionary<string, WTStatus> Statuses { get; private set; }

	/// <summary>
	/// A map of player IDs to the number of stickers they have earned (including claimable duties).
	/// </summary>
	public Dictionary<string, uint> Stickers { get; private set; }

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

	#region Sorter

	private static Comparison<BingoEntry>? _Sorter;

	public Comparison<BingoEntry> Sorter {
		get => _Sorter ?? EntrySorter.DefaultComparison;
		set {
			_Sorter = value;
			_CachedDisplayEntries = null;
		}
	}

	#endregion

	#region Filter Data

	// TODO: Persist filter data in some better way?

	private static bool _FilterNoOpen;

	public bool FilterNoOpen {
		get => _FilterNoOpen;
		set {
			_FilterNoOpen = value;
			_CachedDisplayEntries = null;
		}
	}

	private static List<string> _PlayerFilters = [];

	public List<string> PlayerFilters {
		get => _PlayerFilters;
		set {
			_PlayerFilters = value;
			_CachedDisplayEntries = null;
		}
	}

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

	public PartyBingoState(List<PartyMember> members, IEnumerable<KeyValuePair<string, WTStatus>>? statuses) {
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
	public void UpdateStatuses(IEnumerable<KeyValuePair<string, WTStatus>> statuses) {
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

			// Sanity check, in case someone has completed more duties
			// than they actually need to.
			if (count > 9)
				count = 9;

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
		Dictionary<uint, Dictionary<string, PlayerState.WeeklyBingoTaskStatus>> Orders = [];

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
			if (!sheet.TryGetRow(entry.Key, out var row))
				continue;

			var thing = new BingoEntry(entry.Key, row, entry.Value, PlayerNames, Stickers);
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
#if DEBUG
		Service.Logger.Debug($"Filtering and sorting entries.");
#endif

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

		int playersToggled = 0;
		foreach (string entry in _PlayerFilters) {
			if (PlayerNames.ContainsKey(entry))
				playersToggled++;
		}

		bool filterTypes = typesToggled != 0 && typesToggled != ContentTypes.Count;
		bool filterLevels = levelsToggled != 0 && levelsToggled != LEVEL_GROUPS.Length;
		bool filterPlayers = playersToggled != 0 && playersToggled != PlayerNames.Count;

		if (!filterLevels && !filterTypes && !filterPlayers && !_FilterNoOpen) {
			_CachedDisplayEntries = new(Entries);

		} else {
			_CachedDisplayEntries = [];

			foreach (var entry in Entries) {
				if (_FilterNoOpen && entry.PlayersOpen.Count == 0)
					continue;

				if (filterPlayers) {
					bool matched = false;
					foreach (var who in entry.Players) {
						if (_PlayerFilters.Contains(who.Item1)) {
							matched = true;
							break;
						}
					}
					if (!matched)
						continue;
				}

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

		// Use our sorter.
		_CachedDisplayEntries.Sort(Sorter);
	}

	#endregion

}
