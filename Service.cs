using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace WTSync;

/// <summary>
/// Dalamud Services
/// </summary>
internal class Service {

	[PluginService]
	internal static IDalamudPluginInterface Interface { get; private set; } = null!;

	[PluginService]
	internal static ICommandManager CommandManager { get; private set; } = null!;

	[PluginService]
	internal static ICondition Condition { get; private set; } = null!;

	[PluginService]
	internal static IDataManager DataManager { get; private set; } = null!;

	[PluginService]
	internal static IDtrBar DtrBar { get; private set; } = null!;

	[PluginService]
	internal static IClientState ClientState { get; private set; } = null!;

	[PluginService]
	internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

	[PluginService]
	internal static IAddonEventManager AddonEventManager { get; private set; } = null!;

	[PluginService]
	internal static IFramework Framework { get; private set; } = null!;

	[PluginService]
	internal static INotificationManager NotificationManager { get; private set; } = null!;

	[PluginService]
	public static IObjectTable ObjectTable { get; private set; } = null!;

	[PluginService]
	internal static IGameConfig GameConfig { get; private set; } = null!;

	[PluginService]
	internal static IGameGui GameGui { get; private set; } = null!;

	[PluginService]
	internal static ITextureProvider TextureProvider { get; private set; } = null!;

	[PluginService]
	internal static IPluginLog Logger { get; private set; } = null!;

}
