using System;
using System.Collections.Generic;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace WTSync.Models;

public record WTStatusAndId {
	public string Id { get; set; } = string.Empty;
	public bool Anonymous { get; set; }
	public string? Nickname { get; set; }
	public WTStatus? Status { get; set; }
}

public class WTStatus : IEquatable<WTStatus> {

	public WTStatus() { }

	public DateTime Expires { get; set; }

	public uint Stickers { get; set; }

	public bool[]? StickerPlacement { get; set; }

	public uint SecondChancePoints { get; set; }

	public WTDutyStatus[] Duties { get; set; } = [];

	public bool Equals(WTStatus? other) {
		if (other == null) return false;
		if (other == this) return true;

		if (other.Duties.Length != Duties.Length) return false;
		for (int i = 0; i < Duties.Length; i++) {
			if (!EqualityComparer<WTDutyStatus>.Default.Equals(Duties[i], other.Duties[i]))
				return false;
		}

		if ((other.StickerPlacement == null) != (StickerPlacement == null))
			return false;
		if (StickerPlacement != null) {
			if (other.StickerPlacement!.Length != StickerPlacement.Length)
				return false;
			for (int i = 0; i < StickerPlacement.Length; i++) {
				if (other.StickerPlacement[i] != StickerPlacement[i])
					return false;
			}
		}

		return Expires.Equals(other.Expires)
			&& Stickers == other.Stickers
			&& SecondChancePoints == other.SecondChancePoints;
	}

	public override int GetHashCode() {
		return HashCode.Combine(Expires, Stickers, SecondChancePoints, Duties, StickerPlacement);
	}

	public override bool Equals(object? obj) {
		if (obj is WTStatus other)
			return Equals(other);
		return false;
	}
}

public record struct WTDutyStatus {

	public uint Id { get; set; }

	public PlayerState.WeeklyBingoTaskStatus Status { get; set; }

}
