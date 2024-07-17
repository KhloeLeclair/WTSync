using System.Numerics;

using Dalamud;
using Dalamud.Interface.Windowing;

using ImGuiNET;

namespace WTSync.UI;

internal class SettingsWindow : Window {

	private readonly Plugin Plugin;

	private Configuration Config => Plugin.Config;

	private string[] Sides = [];

	public SettingsWindow(Plugin plugin) : base(
		"WTSync Settings",
		ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize
	) {
		Plugin = plugin;

		Size = new Vector2(400, 100);
		SizeCondition = ImGuiCond.Appearing;
	}

	public void OpenSettings() {
		IsOpen = true;
		WindowName = Localization.Localize("gui.settings", "WTSync Settings");

		Sides = [
			Localization.Localize("gui.setting.attach-left", "Left"),
			Localization.Localize("gui.setting.attach-none", "None"),
			Localization.Localize("gui.setting.attach-right", "Right")
		];
	}

	public override void Draw() {
		bool openWith = Config.OpenWithWT;
		if (ImGui.Checkbox(Localization.Localize("gui.setting.open-with", "Open with Wondrous Tails"), ref openWith)) {
			Config.OpenWithWT = openWith;
			Config.Save();
			Plugin.MainWindow.UpdateFlags();
		}

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.setting.open-with.tip", "When this is enabled, the main WTSync window will open automatically whenever you open the Wondrous Tails window."));

		ImGui.Indent();
		int side = Config.AttachToWT + 1;
		if (ImGui.Combo(Localization.Localize("gui.setting.attach-to", "Attach to Side"), ref side, Sides, 3)) {
			Config.AttachToWT = side - 1;
			Config.Save();
			Plugin.MainWindow.UpdateFlags();
		}

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.setting.attach-to", "When this is set to Left or Right and Wondrous Tails is open, the WTSync window will be automatically repositioned to be next to Wondrous Tails on that side."));

		ImGui.Unindent();

		ImGui.Spacing();

		bool showExpiration = Config.ShowExpiration;
		if (ImGui.Checkbox(Localization.Localize("gui.setting.show-expiration", "Show Expiration Column"), ref showExpiration)) {
			Config.ShowExpiration = showExpiration;
			Config.Save();
		}

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.setting.show-expiration.tip", "When this is enabled, an expiration date column will be displayed in the player list at the top of the WTSync window."));

		bool showSCP = Config.ShowSCP;
		if (ImGui.Checkbox(Localization.Localize("gui.setting.show-scp", "Show Second Chance Points Column"), ref showSCP)) {
			Config.ShowSCP = showSCP;
			Config.Save();
		}

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Localization.Localize("gui.setting.show-scp.tip", "When this is enabled, a second chance points column will be displayed in the player list at the top of the WTSync window."));


	}
}
