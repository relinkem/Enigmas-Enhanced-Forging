#if DEV
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace EnigmasEnhancedForging
{
    // Developer tool, read-only, changes nothing in the game. Not compiled yet, check it builds when the project exists.
    // Console command:
    //   forgedump
    // Lists every prefab in the game that has a Smelter component (smelter, blast furnace, charcoal kiln, and any
    // station other mods add) with all of its public fields, including the accepted input to output list
    // (m_conversion), fuel item, caps and timing. Also lists crafting stations (forge, black forge, artisan table).
    // Run it in the modded profile too, so other mods' additions show up.
    // Output: BepInEx\EnigmasEnhancedForging\dump\stations.txt
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class ForgeDumpCommandPatch
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand(
                "forgedump",
                "Save smelter, blast furnace, kiln and crafting station data to a text file. Developer use.",
                args => Run(args));
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            Terminal ctx = args.Context;
            if (ctx == null) return;
            if (ZNetScene.instance == null) { ctx.AddString("forgedump: load into a world first."); return; }

            var sb = new StringBuilder();
            int smelters = 0, stations = 0;

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                try
                {
                    Smelter smelter = prefab.GetComponent<Smelter>();
                    if (smelter != null)
                    {
                        smelters++;
                        sb.AppendLine("== SMELTER prefab: " + prefab.name);
                        DumpPiece(prefab, sb);
                        DumpFields(smelter, sb);
                        sb.AppendLine();
                    }

                    CraftingStation station = prefab.GetComponent<CraftingStation>();
                    if (station != null)
                    {
                        stations++;
                        sb.AppendLine("== CRAFTING STATION prefab: " + prefab.name);
                        DumpPiece(prefab, sb);
                        DumpFields(station, sb);
                        sb.AppendLine();
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine("!! " + prefab.name + " failed: " + e.Message);
                    sb.AppendLine();
                }
            }

            string dir = Path.Combine(Paths.BepInExRootPath, "EnigmasEnhancedForging", "dump");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "stations.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            ctx.AddString("forgedump: " + smelters + " smelters and " + stations + " crafting stations written to " + file);
        }

        // What it costs to build and which station it needs.
        private static void DumpPiece(GameObject prefab, StringBuilder sb)
        {
            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null) return;
            sb.AppendLine("piece name: " + piece.m_name);
            sb.AppendLine("needs station: " + (piece.m_craftingStation != null ? piece.m_craftingStation.gameObject.name : "none"));
            if (piece.m_resources != null)
                foreach (Piece.Requirement req in piece.m_resources)
                    if (req != null && req.m_resItem != null)
                        sb.AppendLine("build cost: " + req.m_resItem.gameObject.name + " x" + req.m_amount);
        }

        // Every public instance field of the component, by reflection, so it keeps working when the game adds fields.
        private static void DumpFields(object obj, StringBuilder sb)
        {
            foreach (FieldInfo f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value;
                try { value = f.GetValue(obj); }
                catch (Exception) { continue; }
                sb.AppendLine(f.Name + ": " + Format(value, 0));
            }
        }

        private static string Format(object v, int depth)
        {
            if (v == null) return "null";
            if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return v.ToString();
            if (v is ItemDrop) return ((ItemDrop)v).gameObject.name;
            if (v is UnityEngine.Object) return ((UnityEngine.Object)v).name;
            if (v is IList)
            {
                var parts = new StringBuilder("[");
                bool first = true;
                foreach (object item in (IList)v)
                {
                    if (!first) parts.Append(", ");
                    first = false;
                    parts.Append(Format(item, depth + 1));
                }
                return parts.Append("]").ToString();
            }
            // A small plain class, such as Smelter.ItemConversion with m_from and m_to.
            if (depth >= 2) return v.GetType().Name;
            var inner = new StringBuilder("{");
            bool firstField = true;
            foreach (FieldInfo f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object fv;
                try { fv = f.GetValue(v); }
                catch (Exception) { continue; }
                if (!firstField) inner.Append(", ");
                firstField = false;
                inner.Append(f.Name).Append("=").Append(Format(fv, depth + 1));
            }
            return inner.Append("}").ToString();
        }
    }
}
#endif
