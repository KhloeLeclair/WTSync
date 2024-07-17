using System;

using Dalamud.Configuration;

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

	public void Save() {
		if (Plugin.Instance.Config == this)
			Service.Interface.SavePluginConfig(this);
	}

}
