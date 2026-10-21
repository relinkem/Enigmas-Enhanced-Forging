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
    // Writes a list of every one-shot sound effect prefab in the game, with its clips, length, loudness and a rough
    // brightness number, so a finish sound can be picked by name and tried via FinishHissPrefab.
    // Off by default: turn on StationConfig.DumpSounds, start the game once, then read the file named in the log.
    // Console command for trying sounds: press F5, type  epsound sfx_kiln_produce  and press Enter.
    // Several names can be given at once. The sound is played at your feet with the same pitch and fades as the
    // finish sound (FinishSoundPitch and FinishSoundVolume), so what you hear is what a finished bar would sound like.
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class SoundCommand
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("epsound", "Play a sound prefab with the Enhanced Forging finish-sound shaping. Usage: epsound <prefab> [<prefab> ...]",
                args =>
                {
                    Terminal ctx = args.Context;
                    if (ctx == null) return;
                    if (ZNetScene.instance == null || Player.m_localPlayer == null) { ctx.AddString("epsound: load into a world first."); return; }
                    if (args.Length < 2) { ctx.AddString("Usage: epsound <prefab> [<prefab> ...]"); return; }
                    for (int i = 1; i < args.Length; i++)
                    {
                        GameObject prefab = ZNetScene.instance.GetPrefab(args[i]);
                        if (prefab == null) { ctx.AddString("epsound: no prefab named '" + args[i] + "'."); continue; }
                        Transform t = Player.m_localPlayer.transform;
                        StationContainer.ShapeSound(UnityEngine.Object.Instantiate(prefab, t.position, t.rotation), 1f);
                        ctx.AddString("Playing " + args[i]);
                    }
                });
        }
    }

    // Debug command: press F5, type  epaudio  (or  epaudio 80  for a wider radius) and press Enter.
    // Lists every sound that is playing near you (where it sits in the world, how far away, how 3D it is, loop or not)
    // and every converted station (position, running state, fire object, animators), in the console and the BepInEx log.
    // Run it a few times while walking around to see whether a sound's position moves.
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class AudioDebugCommand
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("epaudio", "List the sounds playing near you and the Enhanced Forging stations. Usage: epaudio [radius]",
                args =>
                {
                    Terminal ctx = args.Context;
                    if (ctx == null) return;
                    if (Player.m_localPlayer == null) { ctx.AddString("epaudio: load into a world first."); return; }
                    float radius = 40f;
                    if (args.Length > 1) float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
                    Vector3 me = Player.m_localPlayer.transform.position;
                    var lines = new List<string>();
                    lines.Add("epaudio: player at " + P(me) + ", radius " + radius);

                    foreach (StationContainer st in UnityEngine.Object.FindObjectsOfType<StationContainer>())
                        lines.Add(st.DebugReport(me));

                    int n = 0;
                    foreach (AudioSource a in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                    {
                        if (a == null || !a.isPlaying || a.clip == null) continue;
                        float d = Vector3.Distance(me, a.transform.position);
                        if (a.spatialBlend > 0.5f && d > radius) continue;
                        n++;
                        Transform root = a.transform.root;
                        lines.Add("  sound " + a.clip.name + " on " + PathOf(a.transform) + " at " + P(a.transform.position) +
                                  " (root " + root.name + " at " + P(root.position) + ")" +
                                  " dist " + d.ToString("0.0") + ", 3D " + a.spatialBlend.ToString("0.00") +
                                  ", vol " + a.volume.ToString("0.00") + ", pitch " + a.pitch.ToString("0.00") +
                                  ", range " + a.minDistance.ToString("0") + "-" + a.maxDistance.ToString("0") +
                                  (a.loop ? ", loop" : "") + (a.mute ? ", MUTED" : ""));
                    }
                    lines.Add("epaudio: " + n + " playing sound(s) in range.");
                    foreach (string l in lines) { ctx.AddString(l); Plugin.Log.LogInfo(l); }
                });
        }

        private static string P(Vector3 v) { return "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")"; }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            int guard = 0;
            while (t.parent != null && guard++ < 8) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class SoundDump
    {
        private static bool _done;

        private static void Postfix(ZNetScene __instance)
        {
            if (_done || StationConfig.DumpSounds == null || !StationConfig.DumpSounds.Value) return;
            _done = true;
            try
            {
                var rows = new List<string>();
                foreach (GameObject prefab in __instance.m_prefabs)
                {
                    if (prefab == null) continue;
                    ZSFX[] sfx = prefab.GetComponentsInChildren<ZSFX>(true);
                    if (sfx.Length == 0) continue;
                    foreach (ZSFX z in sfx)
                    {
                        if (z.m_audioClips == null || z.m_audioClips.Length == 0) continue;
                        AudioSource src = z.GetComponent<AudioSource>();
                        bool loop = src != null && src.loop;
                        foreach (AudioClip c in z.m_audioClips)
                        {
                            if (c == null) continue;
                            string stats = Analyse(c);
                            rows.Add(string.Format("{0}\t{1}\t{2:0.00}s\tvol {3:0.00}-{4:0.00}\tpitch {5:0.00}-{6:0.00}\t{7}\t{8}",
                                prefab.name, c.name, c.length, z.m_minVol, z.m_maxVol, z.m_minPitch, z.m_maxPitch,
                                loop ? "LOOP" : "oneshot", stats));
                        }
                    }
                }
                rows.Sort(StringComparer.OrdinalIgnoreCase);

                var sb = new StringBuilder();
                sb.AppendLine("prefab\tclip\tlength\tvolume\tpitch\tkind\tloudness(rms) / brightness(0 low .. 1 hissy) / clip peak");
                foreach (string r in rows) sb.AppendLine(r);

                string path = Path.Combine(Paths.ConfigPath, "EnhancedForging_SoundDump.txt");
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo("Sound dump written: " + path + " (" + rows.Count + " clips)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Sound dump failed: " + e.Message);
            }
        }

        // Rough numbers only: average loudness, and how fast the wave crosses zero (more crossings = hissier/brighter).
        private static string Analyse(AudioClip c)
        {
            try
            {
                if (c.length > 8f || c.samples <= 0) return "(not analysed)";
                int ch = Mathf.Max(1, c.channels);
                float[] data = new float[c.samples * ch];
                // Called through reflection so the build does not need the newer Span-based overloads.
                System.Reflection.MethodInfo getData = typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
                if (getData == null || !(bool)getData.Invoke(c, new object[] { data, 0 })) return "(no data)";
                double sum = 0; int cross = 0; float peak = 0f; float prev = data[0];
                for (int i = 0; i < data.Length; i += ch)
                {
                    float s = data[i];
                    sum += s * s;
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                    if ((s >= 0f) != (prev >= 0f)) cross++;
                    prev = s;
                }
                int n = Mathf.Max(1, data.Length / ch);
                float rms = (float)Math.Sqrt(sum / n);
                float zcr = Mathf.Clamp01((cross / (float)n) * 4f); // crossings per sample, scaled
                return string.Format("rms {0:0.000} / bright {1:0.00} / peak {2:0.00}", rms, zcr, peak);
            }
            catch { return "(not analysed)"; }
        }
    }
}
#endif
