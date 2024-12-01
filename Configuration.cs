using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Configuration;
using Dalamud.Interface.Colors;

using WTSync.Models;

namespace WTSync;

[Serializable]
public class Configuration : IPluginConfiguration {

	public int Version { get; set; } = 0;

	public bool AcceptedTerms { get; set; } = false;

	public string ServerUrl { get; set; } = "https://wtsync.khloeleclair.dev"; //"http://127.0.0.1:8787";

	public bool OpenWithWT { get; set; } = true;

	// -1 = left, 0 = none, 1 = right
	public int AttachToWT { get; set; } = 1;

	public bool ShowExpiration { get; set; } = false;

	public bool ShowSCP { get; set; } = true;

	public float ImageScale { get; set; } = 1.0f;

	public bool OptOutAnalytics { get; set; } = false;

	public bool ClickableDuties { get; set; } = true;

	public bool RandomDutyOnClick { get; set; } = false;

	// Server Bar

	public bool HideBarIfNoSticker { get; set; } = true;

	public int BarColorMode { get; set; } = 0;

	public Vector4 BarColorInDuty { get; set; } = Helpers.BAR_GREEN;

	public Vector4 BarColorInDutyEdge { get; set; } = Helpers.BLACK;

	public Vector4 BarColorDutyClaimed { get; set; } = Helpers.BAR_ORANGE;
	public Vector4 BarColorDutyClaimedEdge { get; set; } = Helpers.BLACK;

	// Colors

	public Vector4 ColorMaxSecondChancePoints { get; set; } = ImGuiColors.DalamudYellow;

	public Vector4 ColorMaxStickers { get; set; } = ImGuiColors.ParsedOrange;

	public Vector4 ColorButtonActive { get; set; } = ImGuiColors.ParsedOrange;

	public Vector4 ColorDutyAvailable { get; set; } = ImGuiColors.ParsedGreen;

	public Vector4 ColorLevelLabel { get; set; } = ImGuiColors.ParsedBlue;

	// Sorting

	public int LastSort { get; set; } = -1;

	public List<CustomSort> CustomSorts { get; set; } = [];


	public void Save() {
		if (Plugin.Instance.Config == this)
			Service.Interface.SavePluginConfig(this);
	}

}
