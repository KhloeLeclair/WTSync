using System.Numerics;

using Dalamud;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

using ImGuiNET;

namespace WTSync.UI;

internal class FirstRunWindow : Window {

	private readonly Plugin Plugin;

	private Configuration Config => Plugin.Config;

	public FirstRunWindow(Plugin plugin) : base(
		"WTSync##WTSyncFirstRun",
		ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize
	) {
		Plugin = plugin;

		Size = new Vector2(400, 300);
		SizeCondition = ImGuiCond.Appearing;

		SizeConstraints = new() {
			MinimumSize = new Vector2(400, 100)
		};

	}

	public override void OnClose() {
		base.OnClose();

		if (Plugin.FirstRunWindow == this)
			Plugin.CloseFirstRunWindow();
	}

	public override void Draw() {

		string label = Localization.Localize("gui.first-run.welcome", "Welcome to WTSync!");
		ImGui.TextColored(ImGuiColors.ParsedOrange, label);

		string terms = Localization.Localize("gui.first-run.terms", "WTSync operates by uploading your Wondrous Tails data to a server, from which other clients can download it. To uniquely identify your character, WTSync transmits a hash of your character's name and home world.\n\nBy agreeing to use WTSync, you agree that your characters' names and home worlds, as well as details of your Wondrous Tails state, may be stored on our server and shared with other users of the WTSync service.\n\nCurious exactly what it looks like? WTSync, as well as its server, are open-source! Contributions are welcome.");

		ImGui.Spacing();

		ImGui.TextWrapped(terms);

		ImGui.Spacing();

		label = Localization.Localize("gui.first-run.no-agree", "If you do not agree, please remove this plug-in from Dalamud.");
		ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
		ImGui.TextWrapped(label);
		ImGui.PopStyleColor();

		ImGui.Spacing();

		if (ImGui.Button(Localization.Localize("gui.first-run.agree", "I agree to upload and share my data."))) {
			Config.AcceptedTerms = true;
			Config.Save();
			IsOpen = false;
		}

	}

}
