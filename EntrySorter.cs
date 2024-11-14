using System;
using System.Collections.Generic;

using Dalamud;

using WTSync.Models;

namespace WTSync;


public static class EntrySorter {

	private static readonly Dictionary<string, Comparison<BingoEntry>> SortingMethods = new();

	private static readonly Dictionary<string, Func<string>> SortingNames = new();

	private static int NullComparison(BingoEntry a, BingoEntry b) => 0;

	public static int DefaultComparison(BingoEntry a, BingoEntry b) {
		int aPlayers = a.PlayersOpen.Count;
		int bPlayers = b.PlayersOpen.Count;

		// First, sort by the number of players with the duty open (descending)
		if (aPlayers != bPlayers)
			return bPlayers.CompareTo(aPlayers);

		// Next, sort by minimum level (ascending) if there were players.
		if (aPlayers > 0) {
			if (a.MinLevel != b.MinLevel)
				return a.MinLevel.CompareTo(b.MinLevel);
		}

		// Third, sort by total number of players (descending)
		aPlayers += a.PlayersClaimable.Count + a.PlayersClaimed.Count;
		bPlayers += b.PlayersClaimable.Count + b.PlayersClaimed.Count;

		if (aPlayers != bPlayers)
			return bPlayers.CompareTo(aPlayers);

		// Next, sort by minimum level (ascending) without a player check
		if (a.MinLevel != b.MinLevel)
			return a.MinLevel.CompareTo(b.MinLevel);

		// Finally, sort by name (ascending)
		return a.DisplayName.CompareTo(b.DisplayName);
	}

	public readonly record struct SortingEntry(string key, bool descending);

	#region Build Sorter

	public static Comparison<BingoEntry> BuildSorter(IEnumerable<SortingEntry> entries) {

		List<Comparison<BingoEntry>> sorters = [];

		if (entries != null)
			foreach (var entry in entries) {
				if (!SortingMethods.TryGetValue(entry.key, out var sorter))
					continue;

				if (entry.descending)
					sorters.Add((a, b) => -sorter(a, b));
				else
					sorters.Add(sorter);
			}

		if (sorters.Count == 0)
			return NullComparison;

		var comparisons = sorters.ToArray();

		return (a, b) => {
			foreach (var cmp in comparisons) {
				int result = cmp(a, b);
				if (result != 0)
					return result;
			}

			return 0;
		};
	}

	#endregion

	#region Define Sorting Methods

	static EntrySorter() {

		Define("players-open", "Player Count (Duty Available)", (a, b) =>
			a.PlayersOpen.Count.CompareTo(b.PlayersOpen.Count)
		);

		Define("players", "Player Count (Total)", (a, b) =>
			a.Players.Count.CompareTo(b.Players.Count)
		);

		Define("min-level", "Minimum Level", (a, b) =>
			a.MinLevel.CompareTo(b.MinLevel)
		);

		Define("max-level", "Maximum Level", (a, b) =>
			a.MaxLevel.CompareTo(b.MaxLevel)
		);

		Define("name", "Display Name", (a, b) =>
			a.DisplayName.CompareTo(b.DisplayName)
		);

		Define("min-level.players", "Minimum Level (if Players have Duty Available)", (a, b) => {
			if (a.Players.Count == 0 && b.Players.Count == 0)
				return 0;
			return a.MinLevel.CompareTo(b.MinLevel);
		});

		Define("max-level.players", "Maximum Level (if Players have Duty Available)", (a, b) => {
			if (a.Players.Count == 0 && b.Players.Count == 0)
				return 0;
			return a.MaxLevel.CompareTo(b.MaxLevel);
		});

	}

	public static bool Define(string key, string i18n_key, string title, Comparison<BingoEntry> sorter) {
		return Define(
			key,
			() => Localization.Localize(i18n_key, title),
			sorter
		);
	}

	public static bool Define(string key, string title, Comparison<BingoEntry> sorter) {
		return Define(
			key,
			() => Localization.Localize($"sort.{key}", title),
			sorter
		);
	}

	public static bool Define(string key, Func<string> title, Comparison<BingoEntry> sorter) {
		lock (SortingMethods) {
			if (SortingMethods.ContainsKey(key))
				return false;

			SortingMethods[key] = sorter;
			SortingNames[key] = title;
		}

		return true;
	}

	#endregion

}

