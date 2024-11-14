using System.Collections.Generic;

using Dalamud;

namespace WTSync.Models;

public record CustomSort {

	public string Name { get; set; } = string.Empty;

	public List<EntrySorter.SortingEntry> Entries { get; set; } = new();

	public string GetName() { return GetName(this); }

	public static string GetName(CustomSort? sort) {
		if (sort == null)
			return Localization.Localize("sort.default", "Default");
		if (string.IsNullOrWhiteSpace(sort.Name))
			return Localization.Localize("sort.unnamed", "(unnamed)");
		return sort.Name;
	}

}
