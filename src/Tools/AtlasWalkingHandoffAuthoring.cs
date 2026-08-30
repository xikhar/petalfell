using System;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Node-free mechanical checks for the walking-window planner and hysteresis.
/// Terrain safety remains the responsibility of TryResolveExactLanding; these
/// cases prove every directional address transition without opening Godot UI or
/// allocating another continent-sized structure.
/// </summary>
public static class AtlasWalkingHandoffAuthoring
{
	public static AtlasWalkingHandoffVerification Verify(WorldAtlasDefinition atlas,
		int triggerMargin, int rearmMargin, int cooldownFrames)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		if (columns < 7 || rows < 7)
			throw new InvalidOperationException(
				$"walking handoff verification requires at least 7x7 sectors, got {columns}x{rows}");
		var current = new AtlasMosaicBounds(3, 3, 4, 4);
		float minX = current.MinSectorX * atlas.SectorSize;
		float minZ = current.MinSectorZ * atlas.SectorSize;
		float maxX = (current.MaxSectorX + 1) * atlas.SectorSize;
		float maxZ = (current.MaxSectorZ + 1) * atlas.SectorSize;
		float centreX = (minX + maxX) * .5f;
		float centreZ = (minZ + maxZ) * .5f;

		int cardinal = 0, corners = 0;
		Expect("west", minX + triggerMargin, centreZ, -1, 0,
			AtlasWindowEdge.West, AtlasWindowEdge.West); cardinal++;
		Expect("east", maxX - triggerMargin, centreZ, 1, 0,
			AtlasWindowEdge.East, AtlasWindowEdge.East); cardinal++;
		Expect("north", centreX, minZ + triggerMargin, 0, -1,
			AtlasWindowEdge.North, AtlasWindowEdge.North); cardinal++;
		Expect("south", centreX, maxZ - triggerMargin, 0, 1,
			AtlasWindowEdge.South, AtlasWindowEdge.South); cardinal++;
		Expect("north-west", minX + triggerMargin, minZ + triggerMargin, -1, -1,
			AtlasWindowEdge.North | AtlasWindowEdge.West,
			AtlasWindowEdge.North | AtlasWindowEdge.West); corners++;
		Expect("north-east", maxX - triggerMargin, minZ + triggerMargin, 1, -1,
			AtlasWindowEdge.North | AtlasWindowEdge.East,
			AtlasWindowEdge.North | AtlasWindowEdge.East); corners++;
		Expect("south-east", maxX - triggerMargin, maxZ - triggerMargin, 1, 1,
			AtlasWindowEdge.South | AtlasWindowEdge.East,
			AtlasWindowEdge.South | AtlasWindowEdge.East); corners++;
		Expect("south-west", minX + triggerMargin, maxZ - triggerMargin, -1, 1,
			AtlasWindowEdge.South | AtlasWindowEdge.West,
			AtlasWindowEdge.South | AtlasWindowEdge.West); corners++;

		// A corner on the west atlas boundary must still advance south while the
		// impossible west component is clamped away.
		var westEdge = new AtlasMosaicBounds(0, 3, 1, 4);
		float westMinX = 0f;
		float westMaxZ = (westEdge.MaxSectorZ + 1) * atlas.SectorSize;
		AtlasWalkingTransition partial = EvaluateTransition(westEdge,
			westMinX + triggerMargin, westMaxZ - triggerMargin);
		if (partial.To.MinSectorX != westEdge.MinSectorX ||
		    partial.To.MinSectorZ != westEdge.MinSectorZ + 1 ||
		    partial.TriggeredEdges != (AtlasWindowEdge.West | AtlasWindowEdge.South) ||
		    partial.ShiftedEdges != AtlasWindowEdge.South)
			throw new InvalidOperationException(
				$"west-edge corner planned {partial.From}->{partial.To} " +
				$"triggered {partial.TriggeredEdges} shifted {partial.ShiftedEdges}");

		// An outer-only corner refuses once and stays latched even after cooldown;
		// repeated frames may not emit another transition/refusal until the player
		// has moved back through the rearm band.
		var outer = new AtlasMosaicBounds(0, 0, 1, 1);
		var refusalLatch = new AtlasWalkingHandoffLatch(cooldownFrames);
		AtlasWalkingHandoffDecision outerDecision = refusalLatch.Evaluate(atlas, outer,
			triggerMargin, triggerMargin, triggerMargin, rearmMargin,
			out AtlasWalkingTransition outerTransition, out string outerRefusal);
		if (outerDecision != AtlasWalkingHandoffDecision.Refused ||
		    outerTransition.TriggeredEdges !=
		    (AtlasWindowEdge.North | AtlasWindowEdge.West) || outerRefusal == null)
			throw new InvalidOperationException(
				$"outer corner returned {outerDecision}: {outerRefusal ?? "no refusal"}");
		int suppressed = 0;
		for (int i = 0; i < cooldownFrames; i++)
		{
			AtlasWalkingHandoffDecision repeated = refusalLatch.Evaluate(atlas, outer,
				triggerMargin, triggerMargin, triggerMargin, rearmMargin,
				outerTransition.TriggeredEdges,
				out _, out _);
			if (repeated != AtlasWalkingHandoffDecision.Suppressed)
				throw new InvalidOperationException(
					$"outer refusal thrashed on repeat {i}: {repeated}");
			suppressed++;
		}
		for (int i = 0; i < 3; i++)
		{
			AtlasWalkingHandoffDecision ignored = refusalLatch.Evaluate(atlas, outer,
				triggerMargin, triggerMargin, triggerMargin, rearmMargin,
				outerTransition.TriggeredEdges, out _, out _);
			if (ignored != AtlasWalkingHandoffDecision.None)
				throw new InvalidOperationException(
					$"blocked outer edge retriggered after cooldown {i}: {ignored}");
		}

		// A completed east transition is suppressed through cooldown, rearms in the
		// deeper band of the new window, then permits one deliberate west return.
		var successLatch = new AtlasWalkingHandoffLatch(cooldownFrames);
		AtlasWalkingHandoffDecision first = successLatch.Evaluate(atlas, current,
			maxX - triggerMargin, centreZ, triggerMargin, rearmMargin,
			out AtlasWalkingTransition east, out _);
		if (first != AtlasWalkingHandoffDecision.Transition)
			throw new InvalidOperationException($"east no-thrash setup returned {first}");
		successLatch.Complete(east);
		for (int i = 0; i < cooldownFrames; i++)
		{
			AtlasWalkingHandoffDecision cooling = successLatch.Evaluate(atlas, east.To,
				maxX - triggerMargin, centreZ, triggerMargin, rearmMargin,
				out _, out _);
			if (cooling != AtlasWalkingHandoffDecision.Suppressed)
				throw new InvalidOperationException(
					$"completed transition escaped cooldown on repeat {i}: {cooling}");
			suppressed++;
		}
		AtlasWalkingHandoffDecision rearmed = successLatch.Evaluate(atlas, east.To,
			maxX - triggerMargin, centreZ, triggerMargin, rearmMargin,
			out _, out _);
		if (rearmed != AtlasWalkingHandoffDecision.None || !successLatch.Armed)
			throw new InvalidOperationException(
				$"completed transition did not rearm in the new safe band: {rearmed}");
		float returnX = east.To.MinSectorX * atlas.SectorSize + triggerMargin;
		AtlasWalkingHandoffDecision returning = successLatch.Evaluate(atlas, east.To,
			returnX, centreZ, triggerMargin, rearmMargin,
			out AtlasWalkingTransition west, out _);
		if (returning != AtlasWalkingHandoffDecision.Transition || west.To != current)
			throw new InvalidOperationException(
				$"rearmed west return produced {returning} {west.From}->{west.To}");

		return new AtlasWalkingHandoffVerification(cardinal, corners,
			1, 1, suppressed, 1);

		void Expect(string label, float x, float z, int dx, int dz,
			AtlasWindowEdge triggered, AtlasWindowEdge shifted)
		{
			AtlasWalkingTransition result = EvaluateTransition(current, x, z);
			var expected = new AtlasMosaicBounds(current.MinSectorX + dx,
				current.MinSectorZ + dz, current.MaxSectorX + dx,
				current.MaxSectorZ + dz);
			if (result.To != expected || result.TriggeredEdges != triggered ||
			    result.ShiftedEdges != shifted)
				throw new InvalidOperationException(
					$"{label} planned {result.From}->{result.To}, " +
					$"triggered {result.TriggeredEdges}, shifted {result.ShiftedEdges}; " +
					$"expected {expected}, {triggered}, {shifted}");
		}

		AtlasWalkingTransition EvaluateTransition(AtlasMosaicBounds from,
			float x, float z)
		{
			var latch = new AtlasWalkingHandoffLatch(cooldownFrames);
			AtlasWalkingHandoffDecision decision = latch.Evaluate(atlas, from, x, z,
				triggerMargin, rearmMargin, out AtlasWalkingTransition result,
				out string refusal);
			if (decision != AtlasWalkingHandoffDecision.Transition)
				throw new InvalidOperationException(
					$"transition {from} at {x},{z} returned {decision}: " +
					$"{refusal ?? "no refusal"}");
			return result;
		}
	}
}

public readonly record struct AtlasWalkingHandoffVerification(
	int CardinalTransitions, int CornerTransitions, int PartialOuterCorners,
	int OuterRefusals, int SuppressedRepeats, int ReturnTransitions);
