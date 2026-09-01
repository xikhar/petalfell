using System;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// THE PROCESS for building reference-scale sites — the general technique the
/// author asked for after three per-site attempts (docs/RUINS.md §5a).
///
/// Every monumental site in the reference images sits on the same kind of
/// ground: great flat-topped SLABS of stone stacked on a natural high point,
/// their sheer faces gashed against each other, rising tier by tier to
/// wherever the monument stands, with stairs notched through the slab fronts.
/// There are no gradual slopes and there is no excavation — the land around
/// the site is exactly what it always was, and the site is built ON it.
///
/// A Massif is one site's earthworks, and it enforces that grammar:
///
///   ADDITIVE, ALWAYS. The final height of every column is
///   max(what the slabs say, what the land already was). A Massif cannot dig,
///   cannot moat, cannot flatten a skirt around itself. Where a slab runs into
///   ground higher than itself it simply disappears into the hillside, which
///   is exactly how real masonry meets a real slope.
///
///   SLABS, NOT SLOPES. A slab is a noise-warped rounded rectangle with one
///   flat top. Height only ever changes at a slab boundary, so every change
///   of level is a sheer face. Stacking overlapped slabs of different tops is
///   what produces the reference's "steep slabs gashed together" — the
///   clefts fall out of the geometry on their own where two warped edges
///   almost meet.
///
///   ONE MATERIAL. Slab fill is the same coursed pale masonry the monuments
///   are built from, so terrain and architecture read as one thing. Tops are
///   cream sand unless a deck or stair says otherwise.
///
///   STAIRS ARE NOTCHES. A stair is stamped through slab fronts after the
///   slabs, replacing their heights along its strip — carved from the mass,
///   never leaned against it.
///
/// Use: construct, then Slab()/Deck()/Stair() in that order, then Apply().
/// The same four calls should be able to shape any site in world-new/ — a
/// water-gate's causeway is a long thin slab, a cliff colonnade's ledge is a
/// slab against a mountainside, a district is many decks on few slabs.
/// </summary>
public sealed class Massif
{
	private readonly Terrain _t;
	private readonly int _cx, _cz, _ext, _side;
	private readonly short[] _target;
	private readonly short[] _orig;
	private readonly byte[] _cap;
	private readonly Noise2D _warp;

	/// <summary>Worked area is a (2·ext+1)² box around (cx, cz). Columns the
	/// slabs never raise are left byte-for-byte alone.</summary>
	public Massif(Terrain t, int cx, int cz, int ext, Noise2D warp)
	{
		_t = t;
		_cx = cx;
		_cz = cz;
		_ext = ext;
		_side = ext * 2 + 1;
		_warp = warp;
		_target = new short[_side * _side];
		_orig = new short[_side * _side];
		_cap = new byte[_side * _side];

		int S = t.Size;
		for (int dz = -ext; dz <= ext; dz++)
		for (int dx = -ext; dx <= ext; dx++)
		{
			int i = (cz + dz) * S + cx + dx;
			int o = (dz + ext) * _side + dx + ext;
			short g = t.Land[i] == 0 ? (short)-1 : t.Level[i];
			_orig[o] = g;
			_target[o] = g;
		}
	}

	/// <summary>
	/// One slab of stone: a rounded rectangle (superellipse) centred at local
	/// (lx, lz), half-extents (ax, az), flat top at absolute height top. The
	/// boundary is warped by a slow field — per-slab phase, so no two slabs
	/// bite the same way — and the exponent keeps corners full rather than
	/// pointed, which is what makes them read as cut stone instead of hills.
	/// </summary>
	public void Slab(float lx, float lz, float ax, float az, int top)
	{
		int x0 = Math.Max(-_ext, (int)(lx - ax - 8)), x1 = Math.Min(_ext, (int)(lx + ax + 8));
		int z0 = Math.Max(-_ext, (int)(lz - az - 8)), z1 = Math.Min(_ext, (int)(lz + az + 8));
		for (int rz = z0; rz <= z1; rz++)
		for (int rx = x0; rx <= x1; rx++)
		{
			int o = (rz + _ext) * _side + rx + _ext;
			if (_target[o] < 0) continue;
			float nx = Math.Abs(rx - lx) / ax, nz = Math.Abs(rz - lz) / az;
			float d = MathF.Pow(nx * nx * nx + nz * nz * nz, 1f / 3f);
			d *= 1f + (_warp.Fbm01((_cx + rx) * 0.035f + lx * 5.1f,
				(_cz + rz) * 0.035f + lz * 3.7f, 3) - 0.5f) * 0.5f;
			if (d >= 1f) continue;
			if (top > _target[o]) _target[o] = (short)top;
		}
	}

	/// <summary>
	/// A masonry deck: a crisp rectangle one course proud of whatever it sits
	/// on, with reference edges — bites where the field is low, single proud
	/// parapet blocks where it is high, corners surviving. The platforms the
	/// monuments actually stand on.
	/// </summary>
	public void Deck(int x0, int x1, int z0, int z1, int level, Noise2D fray)
	{
		for (int rz = z0; rz <= z1; rz++)
		for (int rx = x0; rx <= x1; rx++)
		{
			if (rx < -_ext || rx > _ext || rz < -_ext || rz > _ext) continue;
			int o = (rz + _ext) * _side + rx + _ext;
			if (_target[o] < 0) continue;

			int edge = Math.Min(Math.Min(rx - x0, x1 - rx), Math.Min(rz - z0, z1 - rz));
			bool corner = (rx - x0 <= 1 || x1 - rx <= 1) && (rz - z0 <= 1 || z1 - rz <= 1);
			float f = fray.Fbm01(rx * 0.09f + x0, rz * 0.09f + z0, 2);

			if (edge == 0 && !corner)
			{
				if (f < 0.34f) continue;                 // the bite
				if (f > 0.74f)
				{
					// A parapet block still standing on the rim.
					if (level + 1 > _target[o]) _target[o] = (short)(level + 1);
					_cap[o] = Palette.STONE_PALE;
					continue;
				}
			}
			if (level > _target[o]) _target[o] = (short)level;
			if (_cap[o] == 0 && f > 0.80f) _cap[o] = Palette.PAVING;
		}
	}

	/// <summary>
	/// One flight notched through the slabs: rows descending one block per
	/// tread from the upper level to the lower, then a landing held at the
	/// lower level, cheek walls one course proud, pedestal blocks every sixth
	/// row. Stamp AFTER the slabs it serves — it replaces their heights along
	/// its strip (never below the natural ground; Apply clamps).
	///
	/// (x0, sz): near corner, x0 measured along the width axis, sz along the
	/// travel axis. (ddx, ddz) is the direction of DESCENT.
	/// </summary>
	public void Stair(int x0, int w, int sz, int ddx, int ddz, int from, int to, int landing)
	{
		int wx = ddz != 0 ? 1 : 0, wz = ddx != 0 ? 1 : 0;
		int steps = from - to;
		for (int s = 0; s < steps + landing; s++)
		{
			int level = Math.Max(to, from - s);
			int bx = ddz != 0 ? x0 : sz + s * ddx;
			int bz = ddz != 0 ? sz + s * ddz : x0;
			for (int k = 0; k < w; k++)
				Set(bx + k * wx, bz + k * wz, level, Palette.PAVING);
			int proud = s % 6 == 0 ? 2 : 1;
			Set(bx - wx, bz - wz, level + proud, Palette.STONE_PALE);
			Set(bx + w * wx, bz + w * wz, level + proud, Palette.STONE_PALE);
		}

		void Set(int rx, int rz, int level, byte capId)
		{
			if (rx < -_ext || rx > _ext || rz < -_ext || rz > _ext) return;
			int o = (rz + _ext) * _side + rx + _ext;
			if (_target[o] < 0) return;
			_target[o] = (short)level;
			_cap[o] = capId;
		}
	}

	/// <summary>
	/// Realise the field: fill every raised column with coursed masonry and
	/// cap it, keeping blocks, Heights and Level in step (the Footing
	/// contract). The clamp to the original ground is what makes the whole
	/// system additive — nothing here can lower the land.
	/// </summary>
	public void Apply()
	{
		int S = _t.Size;
		var grid = _t.Grid;
		for (int dz = -_ext; dz <= _ext; dz++)
		for (int dx = -_ext; dx <= _ext; dx++)
		{
			int o = (dz + _ext) * _side + dx + _ext;
			int orig = _orig[o];
			if (orig < 0) continue;
			int tgt = Math.Max(_target[o], orig);
			byte capId = _cap[o];
			if (tgt == orig && capId == 0) continue;

			int x = _cx + dx, z = _cz + dz;
			if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) continue;

			for (int y = orig; y < tgt; y++)
				grid.Set(x, y, z, (y % 5) == 0 ? Palette.STONE : Palette.STONE_PALE);
			grid.Set(x, tgt - 1, z, capId != 0 ? capId : Palette.SAND);
			int i = z * S + x;
			_t.Level[i] = (short)tgt;
			grid.Heights[i] = (short)tgt;
		}
	}
}
