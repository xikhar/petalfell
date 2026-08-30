using System;
using Petalfell.World.Sites;

namespace Petalfell.World;

/// <summary>
/// Dispatch only. Each reference owns a separate voxel blueprint; this registry
/// deliberately provides no shared architectural operations.
/// </summary>
public static class ReferenceSiteBuilder
{
	public static ReferenceSiteStatistics Build(AtlasSectorWindow window,
		ReferenceSiteDefinition site) => site.BuilderId switch
	{
		Reference10GroveCourt.BuilderId => Reference10GroveCourt.Build(window, site),
		_ => throw new InvalidOperationException(
			$"No site-specific voxel blueprint is registered for '{site.BuilderId}'."),
	};
}
