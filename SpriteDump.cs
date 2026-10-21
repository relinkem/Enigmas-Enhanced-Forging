#if DEV
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace EnigmasEnhancedForging
{
    // Developer tool, read-only, changes nothing in the game. Console command:
    //   spritedump [word ...]
    // Finds every sprite loaded in the game whose name contains one of the words (not case sensitive) and saves each
    // as a PNG, plus sprites.txt listing name, size and source texture. Default words: fire flame burn ember coal
    // hearth forge. Used to pick a vanilla flame picture for the Coal row of the station screens.
    // Output: BepInEx\EnigmasEnhancedForging\sprites
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class SpriteDumpCommandPatch
    {
        private static readonly string[] Defaults = { "fire", "flame", "burn", "ember", "coal", "hearth", "forge" };
        private const int MaxSaved = 300;

        private static void Postfix()
        {
            new Terminal.ConsoleCommand(
                "spritedump",
                "[word ...]: save game sprites whose name contains a word as PNG files (default: fire flame burn ember coal hearth forge). Developer use.",
                args => Run(args));
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            Terminal ctx = args.Context;
            if (ctx == null) return;

            var words = new List<string>();
            for (int i = 1; i < args.Length; i++) words.Add(args[i].ToLowerInvariant());
            if (words.Count == 0) words.AddRange(Defaults);

            string dir = Path.Combine(Paths.BepInExRootPath, "EnigmasEnhancedForging", "sprites");
            Directory.CreateDirectory(dir);

            var info = new StringBuilder();
            var seen = new HashSet<string>();
            int found = 0, saved = 0;

            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
            Array.Sort(all, (a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            foreach (Sprite sprite in all)
            {
                if (sprite == null || sprite.texture == null) continue;
                string lower = sprite.name.ToLowerInvariant();
                bool match = false;
                foreach (string w in words) if (lower.Contains(w)) { match = true; break; }
                if (!match) continue;

                Rect r = sprite.textureRect;
                string key = sprite.texture.name + "|" + sprite.name + "|" + r;
                if (!seen.Add(key)) continue;
                found++;
                info.AppendLine(sprite.name + "  " + (int)r.width + "x" + (int)r.height + "  texture: " + sprite.texture.name);

                if (saved >= MaxSaved) continue;
                try
                {
                    Texture2D tex = ArtDumpCommandPatch.Copy(sprite.texture, r);
                    string file = ArtDumpCommandPatch.Safe(sprite.name) + "_" + found + ".png";
                    if (tex != null && ArtDumpCommandPatch.Save(tex, Path.Combine(dir, file))) saved++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("spritedump " + sprite.name + ": " + e.Message);
                }
            }

            File.WriteAllText(Path.Combine(dir, "sprites.txt"), info.ToString(), new UTF8Encoding(false));
            ctx.AddString("spritedump: " + found + " sprites matched, " + saved + " saved with sprites.txt to " + dir);
        }
    }
}
#endif
