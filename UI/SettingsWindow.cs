using System.Numerics;

using Dalamud;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;

using ImGuiNET;

namespace WTSync.UI;

internal class SettingsWindow : Window {

	private readonly Plugin Plugin;

	private Configuration Config => Plugin.Config;

	private string[] Sides = [];
	private string[] BarColorModes = [];

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

		BarColorModes = [
			Localization.Localize("gui.setting.bar-color-none", "None"),
			Localization.Localize("gui.setting.bar-color-icon", "Icon Only"),
			Localization.Localize("gui.setting.bar-color-all", "Everything")
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

		if (ImGui.CollapsingHeader(Localization.Localize("gui.settings.main-window", "Customize Main Window"), ImGuiTreeNodeFlags.DefaultOpen)) {
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

			ImGui.Spacing();

			float value = Config.ImageScale;
			if (ImGui.SliderFloat(Localization.Localize("gui.setting.image-scale", "Image Scale"), ref value, 0f, 2f, "%.1fx")) {
				Config.ImageScale = value;
				Config.Save();
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(Localization.Localize("gui.setting.image-scale.tip", "This lets you adjust the size of the preview image shown for each entry."));

			ImGui.Spacing();
		}

		if (ImGui.CollapsingHeader(Localization.Localize("gui.settings.colors", "Customize Colors"))) {

			var colorSCP = Config.ColorMaxSecondChancePoints;
			if (DrawColorPicker(1, Localization.Localize("gui.setting.color-max-scp", "Capped Second Chance Points"), ref colorSCP, ImGuiColors.DalamudYellow)) {
				Config.ColorMaxSecondChancePoints = colorSCP;
				Config.Save();
				Service.Logger.Debug("Saved max scp!");
			}

			// ---

			var colorStickers = Config.ColorMaxStickers;
			if (DrawColorPicker(2, Localization.Localize("gui.setting.color-max-sticker", "Completed / Max Stickers"), ref colorStickers, ImGuiColors.ParsedOrange)) {
				Config.ColorMaxStickers = colorStickers;
				Config.Save();
			}

			// ---

			var colorFilter = Config.ColorButtonActive;
			if (DrawColorPicker(3, Localization.Localize("gui.setting.color-btn-active", "Filter Active"), ref colorFilter, ImGuiColors.ParsedOrange)) {
				Config.ColorButtonActive = colorFilter;
				Config.Save();
			}

			// ---

			var colorDutyOpen = Config.ColorDutyAvailable;
			if (DrawColorPicker(4, Localization.Localize("gui.setting.color-duty-open", "Duty Available"), ref colorDutyOpen, ImGuiColors.ParsedGreen)) {
				Config.ColorDutyAvailable = colorDutyOpen;
				Config.Save();
			}

			// ---

			var colorLevel = Config.ColorLevelLabel;
			if (DrawColorPicker(5, Localization.Localize("gui.setting.color-level-label", "Level Label"), ref colorLevel, ImGuiColors.ParsedBlue)) {
				Config.ColorLevelLabel = colorLevel;
				Config.Save();
			}

			ImGui.Spacing();
		}

		if (ImGui.CollapsingHeader(Localization.Localize("gui.settings.server-info", "Server Info Bar"))) {
			ImGui.TextWrapped(Localization.Localize("gui.settings.about-server-info", "WTSync displays your current Wondrous Tails state in the Server Info Bar. If you want to disable that behavior, please use Dalamud's settings to hide it."));

			ImGui.Spacing();

			if (ImGui.Button(Localization.Localize("gui.settings.open-dalamud-cfg", "Open Dalamud Settings"))) {
				Service.Interface.OpenDalamudSettingsTo(Dalamud.Interface.SettingsOpenKind.ServerInfoBar);
			}

			ImGui.Spacing();

			int colorMode = Config.BarColorMode;
			if (ImGui.Combo(Localization.Localize("gui.setting.bar-color", "In Duty Color Mode"), ref colorMode, BarColorModes, BarColorModes.Length)) {
				Config.BarColorMode = colorMode;
				Config.Save();
				Plugin.UpdateBar();
			}

			// ---

			var colorInDuty = Config.BarColorInDuty;
			if (DrawColorPicker(6, Localization.Localize("gui.setting.bar-color-picker", "In Duty Color"), ref colorInDuty, Helpers.BAR_GREEN, ImGuiColorEditFlags.NoAlpha)) {
				Config.BarColorInDuty = colorInDuty;
				Config.Save();
				Plugin.UpdateBar();
			}

			// ---

			var edgeInDuty = Config.BarColorInDutyEdge;
			if (DrawColorPicker(7, Localization.Localize("gui.setting.bar-color-picker-edge", "In Duty Edge Color"), ref edgeInDuty, Helpers.BLACK, ImGuiColorEditFlags.NoAlpha)) {
				Config.BarColorInDutyEdge = edgeInDuty;
				Config.Save();
				Plugin.UpdateBar();
			}

			// ---

			var colorClaimedDuty = Config.BarColorDutyClaimed;
			if (DrawColorPicker(8, Localization.Localize("gui.setting.bar-color-picker.claimed", "Claimed Duty Color"), ref colorClaimedDuty, Helpers.BAR_ORANGE, ImGuiColorEditFlags.NoAlpha)) {
				Config.BarColorDutyClaimed = colorClaimedDuty;
				Config.Save();
				Plugin.UpdateBar();
			}

			// ---

			var edgeClaimed = Config.BarColorDutyClaimedEdge;
			if (DrawColorPicker(9, Localization.Localize("gui.setting.bar-color-picker-edge.claimed", "Claimed Edge Color"), ref edgeClaimed, Helpers.BLACK, ImGuiColorEditFlags.NoAlpha)) {
				Config.BarColorDutyClaimedEdge = edgeClaimed;
				Config.Save();
				Plugin.UpdateBar();
			}

		}

	}

	internal bool DrawColorPicker(int id, string label, ref Vector4 color, Vector4 defaultValue, ImGuiColorEditFlags flags = ImGuiColorEditFlags.None) {

		string resetLabel = Localization.Localize("gui.setting.reset", "Reset");
		var resetSize = ImGui.CalcTextSize(resetLabel);
		var style = ImGui.GetStyle();

		float pickerX = ImGui.GetWindowContentRegionMax().X - (32 + resetSize.X + style.WindowPadding.X + style.FramePadding.X);
		float resetX = ImGui.GetWindowContentRegionMax().X - (resetSize.X + style.WindowPadding.X);

		ImGui.Text(label);

		ImGui.SameLine(pickerX);
		var newColor = ImGuiComponents.ColorPickerWithPalette(id, "", color, flags);

		if (newColor != defaultValue) {
			ImGui.SameLine(resetX);
			ImGui.PushID($"reset-color-{id}");
			if (ImGui.Button(resetLabel))
				newColor = defaultValue;
			ImGui.PopID();
		}

		if (newColor != color) {
			color = newColor;
			return true;
		}

		return false;
	}
}
