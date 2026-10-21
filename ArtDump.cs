#if DEV
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace EnigmasEnhancedForging
{
    // Developer tool, not used by the mod itself. Console command:
    //   salvagerart [prefab ...]   saves the inventory icon and every texture of the given items as PNG files
    //                              (default: the four metal scraps) plus an info.txt describing their models.
    // Output goes to BepInEx\EnigmasEnhancedForging\art. Reference material for planning item re-skins; changes nothing.
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class ArtDumpCommandPatch
    {
        private static readonly string[] Defaults = { "BlackMetalScrap", "IronScrap", "CopperScrap", "BronzeScrap" };

        private static void Postfix()
        {
            new Terminal.ConsoleCommand(
                "salvagerart",
                "[prefab ...]: save icons and textures of items as PNG files (default: the four metal scraps). Developer use.",
                args => Run(args));
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            Terminal ctx = args.Context;
            if (ctx == null) return;
            if (ObjectDB.instance == null) { ctx.AddString("salvagerart: load into a world first."); return; }

            var names = new List<string>();
            if (args.Args != null)
                for (int i = 1; i < args.Args.Length; i++) names.Add(args.Args[i]);
            if (names.Count == 0) names.AddRange(Defaults);

            string dir = Path.Combine(Paths.BepInExRootPath, "EnigmasEnhancedForging", "art");
            Directory.CreateDirectory(dir);
            var info = new StringBuilder();
            int saved = 0;

            foreach (string name in names)
            {
                try
                {
                    GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
                    if (prefab == null) { ctx.AddString("salvagerart: no item named " + name); continue; }
                    info.AppendLine("== " + name);
                    saved += DumpItem(prefab, name, dir, info);
                }
                catch (Exception e)
                {
                    ctx.AddString("salvagerart: " + name + " failed: " + e.Message);
                    Plugin.Log.LogWarning("salvagerart " + name + ": " + e);
                }
            }

            File.WriteAllText(Path.Combine(dir, "info.txt"), info.ToString(), new UTF8Encoding(false));
            ctx.AddString("salvagerart: saved " + saved + " pictures and info.txt to " + dir);
        }

        private static int DumpItem(GameObject prefab, string name, string dir, StringBuilder info)
        {
            int saved = 0;

            // Inventory icons.
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop != null && drop.m_itemData.m_shared.m_icons != null)
            {
                Sprite[] icons = drop.m_itemData.m_shared.m_icons;
                for (int i = 0; i < icons.Length; i++)
                {
                    if (icons[i] == null) continue;
                    Texture2D tex = Copy(icons[i].texture, icons[i].textureRect);
                    if (tex != null && Save(tex, Path.Combine(dir, name + "_icon" + i + ".png"))) saved++;
                    info.AppendLine("icon" + i + ": sprite " + icons[i].name + " " + icons[i].textureRect.width + "x" + icons[i].textureRect.height);
                }
            }

            // World model: every renderer, its mesh and the textures of its materials.
            var seen = new HashSet<int>();
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " (" + mf.sharedMesh.vertexCount + " vertices)" : "no mesh";
                info.AppendLine("renderer " + r.gameObject.name + " [" + r.GetType().Name + "] mesh: " + mesh);

                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string color = m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "none";
                    info.AppendLine("  material " + m.name + " shader " + (m.shader != null ? m.shader.name : "?") + " color " + color);
                    foreach (string prop in m.GetTexturePropertyNames())
                    {
                        Texture2D t2 = m.GetTexture(prop) as Texture2D;
                        if (t2 == null) continue;
                        info.AppendLine("    " + prop + ": " + t2.name + " " + t2.width + "x" + t2.height);
                        if (!seen.Add(t2.GetInstanceID())) continue;
                        Texture2D copy = Copy(t2, new Rect(0, 0, t2.width, t2.height));
                        if (copy != null && Save(copy, Path.Combine(dir, name + "_" + Safe(t2.name) + ".png"))) saved++;
                    }
                }
            }
            info.AppendLine();
            return saved;
        }

        internal static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // Textures in the game are usually not readable from code, so draw them into a temporary texture first.
        internal static Texture2D Copy(Texture src, Rect area)
        {
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var full = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                full.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                full.Apply();

                int x = Mathf.Clamp((int)area.x, 0, src.width - 1);
                int y = Mathf.Clamp((int)area.y, 0, src.height - 1);
                int w = Mathf.Clamp((int)area.width, 1, src.width - x);
                int h = Mathf.Clamp((int)area.height, 1, src.height - y);
                if (x == 0 && y == 0 && w == src.width && h == src.height) return full;

                var cut = new Texture2D(w, h, TextureFormat.RGBA32, false);
                cut.SetPixels(full.GetPixels(x, y, w, h));
                cut.Apply();
                UnityEngine.Object.Destroy(full);
                return cut;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // PNG encoding lives in a Unity module this project cannot reference, so it is called by name.
        internal static bool Save(Texture2D tex, string path)
        {
            System.Type conv = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            MethodInfo encode = conv == null ? null : conv.GetMethod("EncodeToPNG", new System.Type[] { typeof(Texture2D) });
            if (encode == null) return false;
            byte[] bytes = encode.Invoke(null, new object[] { tex }) as byte[];
            if (bytes == null) return false;
            File.WriteAllBytes(path, bytes);
            return true;
        }
    }
}
#endif
