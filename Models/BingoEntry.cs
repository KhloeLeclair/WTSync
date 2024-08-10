using System.Collections.Generic;
using System.Linq;

using Dalamud;
using Dalamud.Interface.Textures;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

using Lumina.Excel.GeneratedSheets2;

namespace WTSync.Models;


public record BingoEntry {

	/// <summary>
	/// The ID for this WeeklyBingoOrder
	/// </summary>
	public uint Id { get; }

	/// <summary>
	/// The available data for this WeeklyBingoOrder, which includes an icon and display name
	/// </summary>
	public WeeklyBingoOrderData Data { get; }

	/// <summary>
	/// The list of matching conditions for this WeeklyBingoOrder
	/// </summary>
	public List<ContentFinderCondition> Conditions { get; }

	/// <summary>
	/// A list of content types of the matching conditions for this WeeklyBingoOrder.
	/// </summary>
	public List<ContentType> ContentTypes { get; }

	/// <summary>
	/// A list of player IDs for players that have this order and haven't done it yet.
	/// </summary>
	public List<string> PlayersOpen { get; } = [];

	/// <summary>
	/// A list of player IDs for players that have this order, and have completed it, but haven't claimed it yet
	/// </summary>
	public List<string> PlayersClaimable { get; } = [];

	/// <summary>
	/// A list of player IDs for players that have this order and have completed and claimed it.
	/// </summary>
	public List<string> PlayersClaimed { get; } = [];

	/// <summary>
	/// A list of all players that have this order, their names, and their statuses.
	/// </summary>
	public List<(string, string, PlayerState.WeeklyBingoTaskStatus)> Players { get; } = [];

	/// <summary>
	/// The minimum level of content for this WeeklyBingoOrder.
	/// </summary>
	public uint MinLevel { get; }

	/// <summary>
	/// The maximum level of content for this WeeklyBingoOrder.
	/// </summary>
	public uint MaxLevel { get; }

	public BingoEntry(uint id, WeeklyBingoOrderData data, IEnumerable<KeyValuePair<string, PlayerState.WeeklyBingoTaskStatus>> players, Dictionary<string, string> playerNames, Dictionary<string, uint> playerStickers) {
		Id = id;
		Data = data;
		Conditions = Helpers.GetConditionsForEntry(Data);

		uint min = uint.MaxValue;
		uint max = uint.MinValue;

		ContentTypes = [];

		foreach (var condition in Conditions) {
			uint lvl = condition.ContentType.Row == 9 ? condition.ClassJobLevelSync : condition.ClassJobLevelRequired;
			if (lvl < min)
				min = lvl;
			if (lvl > max)
				max = lvl;

			var ctype = condition.ContentType.Value;
			if (ctype != null && !ContentTypes.Contains(ctype))
				ContentTypes.Add(ctype);
		}

		MinLevel = min;
		MaxLevel = max;

		foreach (var entry in players) {
			uint stickers = playerStickers.GetValueOrDefault(entry.Key);
			var value = entry.Value;

			// If a player already has 9 stickers, they don't need any further
			// duties so make sure to mark any open duties as claimed. This is
			// not quite accurate but it serves well enough for the purpose
			// of not showing their names as green next to a duty.
			if (stickers >= 9 && value == PlayerState.WeeklyBingoTaskStatus.Open)
				value = PlayerState.WeeklyBingoTaskStatus.Claimed;

			if (playerNames.TryGetValue(entry.Key, out string? name))
				Players.Add((entry.Key, name, value));

			switch (value) {
				case PlayerState.WeeklyBingoTaskStatus.Open:
					PlayersOpen.Add(entry.Key);
					break;
				case PlayerState.WeeklyBingoTaskStatus.Claimable:
					PlayersClaimable.Add(entry.Key);
					break;
				case PlayerState.WeeklyBingoTaskStatus.Claimed:
					PlayersClaimed.Add(entry.Key);
					break;
			}
		}

		Players.Sort((a, b) => {
			if (a.Item3 != b.Item3)
				return a.Item3.CompareTo(b.Item3);

			return a.Item2.CompareTo(b.Item2);
		});

	}

	#region Helper Properties

	public ISharedImmediateTexture Icon => Service.TextureProvider.GetFromGameIcon(new GameIconLookup(Data.Icon, hiRes: true));

	private string? _DisplayName;

	public string DisplayName {
		get {
			if (_DisplayName == null) {
				if (Data.Text.Row != 0 && Data.Text.Value is not null)
					_DisplayName = Data.Text.Value.Description.ToString();

				else if (Conditions.Count > 0)
					_DisplayName = Conditions[0].Name.ToTitleCase();

				else
					_DisplayName = Localization.Localize("gui.unknown-order", "Unknown ({id})").Replace("{id}", Id.ToString());
			}

			return _DisplayName;
		}
	}

	private string? _ToolTip;

	public string? ToolTip {
		get {
			if (Conditions.Count == 0)
				return null;

			if (_ToolTip == null) {
				bool showLevel = MinLevel != MaxLevel;

				var conditionLines = showLevel
					? Conditions
						.Select(x => {
							uint lvl = x.ContentType.Row == 9 ? x.ClassJobLevelSync : x.ClassJobLevelRequired;
							return (x.Name.ToTitleCase(), lvl);
						})
						.Where(x => !string.IsNullOrWhiteSpace(x.Item1))
						.DistinctBy(x => x.Item1)
						.Select(x => {
							// TODO: Make this "Lv." translatable.
							return $"{x.Item1} (Lv. {x.Item2})";
						})
					: Conditions
						.Select(x => x.Name.ToTitleCase())
						.Where(x => !string.IsNullOrWhiteSpace(x))
						.Distinct();

				_ToolTip = Localization.Localize("gui.matching-duties", "Matching Duties:") + "\n\n" + string.Join('\n', conditionLines);
			}

			return _ToolTip;
		}
	}

	#endregion

}
