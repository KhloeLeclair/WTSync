namespace WTSync.Models;

internal readonly record struct ShareResult(
	bool Success,
	string? Error,
	string? Url
);
