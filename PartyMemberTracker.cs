// Ignore Spelling: plugin

using System;
using System.Collections.Generic;

using Dalamud.Plugin.Services;

using WTSync.Models;

namespace WTSync;

public sealed class PartyMemberTracker : IDisposable {

	private List<PartyMember> CachedMembers = [];

	public event EventHandler<List<PartyMember>>? MembersChanged;

	public PartyMemberTracker() {
		Service.Framework.Update += OnUpdate;
	}

	public void Dispose() {
		Service.Framework.Update -= OnUpdate;
	}

	public List<PartyMember> Members => CachedMembers;

	private void OnUpdate(IFramework framework) {
		// We do not check party members if not logged in. Clear the cache, too.
		if (!Service.ClientState.IsLoggedIn) {
			CachedMembers = [];
			return;
		}

		// We do not check party members in a duty, but we keep the cache as long
		// as we HAVE a cache.
		if (GameState.IsInDuty && CachedMembers.Count > 0)
			return;

		// Check to see if the number of party members has changed.
		int count = GameState.GetPartyCount();
		int existing = CachedMembers?.Count ?? 0;

		if (count == existing)
			return;

		// Still here? The party changed.
		var members = GameState.ReadPartyMembers();

		// Double check, in case there was a mismatch in GetPartyCount somehow.
		if (members.Count == existing)
			return;

#if DEBUG
		Service.Logger.Debug($"Party list changed!\nExisting: {string.Join(", ", CachedMembers ?? [])}\nNew: {string.Join(", ", members)}");
#endif

		CachedMembers = members;
		MembersChanged?.Invoke(this, members);
	}
}
