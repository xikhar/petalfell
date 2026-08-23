using System;
using System.Runtime.CompilerServices;

namespace Petalfell.Core;

/// <summary>
/// Seeded randomness and value noise.
///
/// Ported verbatim in behaviour from the reference project's core/rng.js. The
/// engine's FastNoiseLite is deliberately NOT used: different noise means a
/// different world, and world stability across sessions and builds is a design
/// requirement, not an implementation detail.
/// </summary>
public sealed class Rng
{
	private uint _s;

	public Rng(int seed)
	{
		// mulberry32's state is the seed itself. Zero is a valid source seed.
		_s = unchecked((uint)seed);
	}

	/// <summary>Uniform [0,1). mulberry32 — small, fast, good enough for scatter.</summary>
	public float Next()
	{
		_s += 0x6D2B79F5u;
		uint t = _s;
		t = (uint)((t ^ (t >> 15)) * (t | 1u));
		t ^= t + (uint)((t ^ (t >> 7)) * (t | 61u));
		return (t ^ (t >> 14)) / 4294967296f;
	}

	public float Range(float lo, float hi) => lo + Next() * (hi - lo);
	public int RangeInt(int lo, int hi) => lo + (int)(Next() * (hi - lo + 1));
	public bool Chance(float p) => Next() < p;

	/// <summary>Three source uniforms, centred on 0 in [-1,1].</summary>
	public float Bell() => (Next() + Next() + Next()) / 1.5f - 1f;

	public T Pick<T>(T[] items) => items[Math.Min(items.Length - 1, (int)(Next() * items.Length))];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	/// <summary>
	/// A stable hash of a string.
	///
	/// NOT string.GetHashCode(). That is randomized per process in .NET Core as
	/// a hash-flooding mitigation, so anything seeded from it produces a
	/// different result every launch — which cost this project a chapter whose
	/// authored towns rebuilt themselves differently on every boot, with every
	/// other stage around them perfectly deterministic. Any identifier that
	/// reaches a seed has to come through here.
	/// </summary>
	public static int StableHash(string text)
	{
		unchecked
		{
			uint h = 2166136261u;
			foreach (char c in text ?? "")
			{
				h ^= c;
				h *= 16777619u;
			}
			return (int)h;
		}
	}

	public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int ClampI(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Smoothstep(float a, float b, float x)
	{
		if (a == b) return x < a ? 0f : 1f;
		float t = Clamp((x - a) / (b - a), 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>
/// Deterministic 2D value noise with fbm and ridge variants.
///
/// Hash-based rather than table-based so a noise field is a pure function of
/// (seed, x, z) — every consumer can sample it in any order, on any thread,
/// and still agree about the world.
/// </summary>
public sealed class Noise2D
{
	private readonly int _seed;

	public Noise2D(int seed) => _seed = seed;

	/// <summary>
	/// Hash to [0,1). Unsigned throughout, because the shifts must be logical.
	///
	/// The reference uses JavaScript's `>>>`. Porting that to C#'s `>>` on an
	/// int is an arithmetic shift, which sign-extends: `h ^ (h >> 16)` then
	/// clears the top bit of every negative value, so the result never reaches
	/// the upper half of the range and every noise field comes out with a mean
	/// of 0.25 instead of 0.5. The symptom is a world with one grass tone, no
	/// biome variety and almost no trees — every threshold downstream sits
	/// above everything the field can produce.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private float Hash(int x, int z)
	{
		unchecked
		{
			uint h = (uint)(x * 0x27d4eb2d) ^ (uint)(z * 0x165667b1) ^
			         (uint)(_seed * unchecked((int)0x9e3779b1));
			h = (h ^ (h >> 15)) * 0x85ebca6bu;
			h = (h ^ (h >> 13)) * 0xc2b2ae35u;
			return (h ^ (h >> 16)) / 4294967296f;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

	/// <summary>Source value noise in [-1,1], quintic-interpolated.</summary>
	public float Value(float x, float z)
	{
		int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
		float fx = x - xi, fz = z - zi;
		fx = Fade(fx);
		fz = Fade(fz);
		float a = Hash(xi, zi), b = Hash(xi + 1, zi);
		float c = Hash(xi, zi + 1), d = Hash(xi + 1, zi + 1);
		return Rng.Lerp(Rng.Lerp(a, b, fx), Rng.Lerp(c, d, fx), fz) * 2f - 1f;
	}

	/// <summary>Source fractal sum. Returns roughly [-1,1].</summary>
	public float Fbm(float x, float z, int octaves = 4)
	{
		float sum = 0f, amp = 1f, norm = 0f, frequency = 1f;
		for (int i = 0; i < octaves; i++)
		{
			sum += Value(x * frequency, z * frequency) * amp;
			norm += amp;
			amp *= 0.5f;
			frequency *= 2f;
		}
		return sum / norm;
	}

	/// <summary>Compatibility view for code not yet transliterated from its former 0..1 field.</summary>
	public float Fbm01(float x, float z, int octaves = 4) => Fbm(x, z, octaves) * 0.5f + 0.5f;

	/// <summary>Ridged fractal — creases where the field crosses its midline.</summary>
	public float Ridge(float x, float z, int octaves = 4)
	{
		float sum = 0f, amp = 1f, norm = 0f, frequency = 1f;
		for (int i = 0; i < octaves; i++)
		{
			float v = 1f - MathF.Abs(Value(x * frequency, z * frequency));
			sum += v * v * amp;
			norm += amp;
			amp *= 0.5f;
			frequency *= 2f;
		}
		return sum / norm;
	}
}
