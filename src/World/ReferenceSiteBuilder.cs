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
		ReferenceSiteDefinition site, int verticalOffset = 0) => site.BuilderId switch
	{
		Reference10GroveCourt.BuilderId => Reference10GroveCourt.Build(window, site,
			verticalOffset),
		Reference1ShallowsGateCauseway.BuilderId =>
			verticalOffset == 0
				? Reference1ShallowsGateCauseway.Build(window, site)
				: throw new InvalidOperationException(
					"Reference 1 does not support a translated quick-runtime datum."),
		_ => throw new InvalidOperationException(
			$"No site-specific voxel blueprint is registered for '{site.BuilderId}'."),
	};
}
