using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Render;

namespace Petalfell.Tools;

/// <summary>
/// The headless-ish capture rig, and the reason the reference project's look
/// got as far as it did.
///
/// Boots the game, waits for streaming and the world to settle, drives the
/// camera to a set of named viewpoints and writes a PNG for each. Deterministic
/// by construction: fixed seed, fixed camera values, fixed frame counts. This
/// is the eye the visual-review loop looks through, so it must not drift.
///
///   godot-mono --path . -- --shots res://../shots/pass1 --only hero,wide
/// </summary>
public static class Capture
{
	public readonly struct Shot
	{
		public readonly string Name;
		public readonly float Distance, Yaw, Pitch;
		/// <summary>What to frame: 0 spawn, 1 tallest cliff nearby, 2 nearest water edge.</summary>
		public readonly int Subject;
		/// <summary>Time of day to force, or negative to leave the clock alone.</summary>
		public readonly float Time;

		public Shot(string name, float distance, float yaw, float pitch, int subject = 0,
			float time = -1f)
		{
			Name = name; Distance = distance; Yaw = yaw; Pitch = pitch; Subject = subject;
			Time = time;
		}
	}

	public static readonly Shot[] Shots =
	{
		// Kept numerically identical to pastel-game/tools/shoot.mjs. A named
		// capture is an inter-engine contract, not merely a pleasant angle.
		new("hero", 40f, 45f, 33.5f),
		new("wide", 72f, 45f, 38f),
		new("close", 27f, 60f, 26f),
		new("bridge", 38f, 30f, 30f, 3),
		new("river", 40f, 115f, 22f, 2),
		new("canopy", 26f, 200f, 20f, 4),
		new("cliffs", 42f, 300f, 28f, 1),
		new("lowsun", 54f, 145f, 14f),
		// A blossom province at play distance. Every other shot frames the
		// spawn, and the spawn is wherever the chapter put it — which may be
		// open sand. The art target lives in the groves, so the review loop
		// needs a viewpoint that is guaranteed to be standing in one.
		new("grove", 62f, 45f, 34f, 5),
		// Open water. The river subject frames a channel, which is often a
		// narrow cut with the banks filling the frame; the lake is the one
		// body guaranteed to give a broad surface to judge.
		new("lake", 58f, 45f, 30f, 6),
		// Somewhere people live. The roads, the houses and the wildlife are all
		// placed away from the spawn by construction, so without a viewpoint that
		// seeks them out the review loop can never see any of them.
		new("ruin", 96f, 45f, 38f, 7),
		// The lit window is the whole life/death signal in this world and had
		// never once been reviewed, because holdouts are rare and every other
		// subject picks whichever site has the most buildings.
		new("holdout", 74f, 45f, 34f, 8),
		new("monument", 110f, 45f, 40f, 9),
		new("tower", 62f, 45f, 30f, 10),
		new("stones", 52f, 45f, 32f, 11),
		new("farmstead", 48f, 45f, 32f, 12),
		// The same holdout around the clock. A day cycle can only be judged as a
		// SEQUENCE — any single frame of it looks plausible, and what goes wrong
		// is the transitions between them.
		new("t_dawn", 74f, 45f, 34f, 8, 0.27f),
		new("t_morning", 74f, 45f, 34f, 8, 0.36f),
		new("t_noon", 74f, 45f, 34f, 8, 0.50f),
		new("t_dusk", 74f, 45f, 34f, 8, 0.76f),
		new("t_night", 74f, 45f, 34f, 8, 0.98f),
	};

	public static (string dir, HashSet<string> only) ParseArgs()
	{
		string dir = null;
		HashSet<string> only = null;
		var args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--shots" && i + 1 < args.Length) dir = args[i + 1];
			else if (args[i].StartsWith("--shots=")) dir = args[i][8..];
			else if (args[i] == "--only" && i + 1 < args.Length)
				only = new HashSet<string>(args[i + 1].Split(','));
			else if (args[i].StartsWith("--only=")) only = new HashSet<string>(args[i][7..].Split(','));
		}
		return (dir, only);
	}

	public static void Place(CameraRig rig, in Shot shot, Vector3 focus)
	{
		float yaw = Mathf.DegToRad(shot.Yaw);
		float pitch = Mathf.DegToRad(shot.Pitch);
		var offset = new Vector3(
			Mathf.Sin(yaw) * Mathf.Cos(pitch),
			Mathf.Sin(pitch),
			Mathf.Cos(yaw) * Mathf.Cos(pitch)) * shot.Distance;
		rig.GlobalPosition = focus + offset;
		rig.LookAt(focus, Vector3.Up);
	}

	public static void Save(Viewport viewport, string dir, string name)
	{
		var image = viewport.GetTexture().GetImage();
		DirAccess.MakeDirRecursiveAbsolute(dir);
		string path = $"{dir}/{name}.png";
		image.SavePng(path);
		GD.Print($"[capture] {path}");
	}
}
