using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Flynn.Feel.Editor
{
    /// <summary>
    /// Deterministic presentation gate — checks the open scene against VibeSpec floors
    /// (VibeSpec.md / VibeTokens). Run after every Codely wiring: Flynn > VibeLint > Scan Scene.
    /// Zero AI, zero tokens: measures what a screenshot-judging model would guess at.
    /// </summary>
    public static class VibeLint
    {
        [MenuItem("Flynn/VibeLint/Scan Scene")]
        public static void ScanScene()
        {
            var sb = new StringBuilder();
            int fails = 0, warns = 0;
            void Fail(string m) { sb.AppendLine("  ✗ FAIL  " + m); fails++; }
            void Warn(string m) { sb.AppendLine("  ⚠ warn  " + m); warns++; }
            void Pass(string m) { sb.AppendLine("  ✓ ok    " + m); }

            var sprites = Object.FindObjectsOfType<SpriteRenderer>();
            var lights  = Object.FindObjectsOfType<Light2D>();
            var breathers = Object.FindObjectsOfType<Breather>();

            // 1. Untinted built-in primitives (the "no CSS" smell).
            var naked = new List<string>();
            foreach (var sr in sprites)
            {
                if (sr.sprite == null) continue;
                bool builtin = AssetDatabase.GetAssetPath(sr.sprite).Contains("unity_builtin");
                bool untinted = ((Color)sr.color).maxColorComponent > 0.98f
                                && Mathf.Abs(sr.color.r - sr.color.g) < 0.02f
                                && Mathf.Abs(sr.color.g - sr.color.b) < 0.02f;
                if (builtin && untinted) naked.Add(sr.gameObject.name);
            }
            if (naked.Count > 0) Fail($"untinted built-in primitives (use VibeSpec tokens): {string.Join(", ", naked)}");
            else Pass("no untinted primitives");

            // 2. Light floor: 1 global + ≥1 accent.
            bool hasGlobal = false, hasAccent = false;
            foreach (var l in lights)
            {
                if (l.lightType == Light2D.LightType.Global) hasGlobal = true;
                else hasAccent = true;
            }
            if (!hasGlobal) Fail("no global Light2D (VibeSpec light floor)");
            else Pass("global light present");
            if (!hasAccent) Warn("no accent point light — add one warm light near a focal object");
            else Pass("accent light present");

            // 3. Motion floor: Breathers exist.
            if (breathers.Length == 0) Warn("no Breather components — nothing idles (motion floor)");
            else Pass($"{breathers.Length} Breather(s) present");

            // 4. Ground vs camera-bg contrast.
            var cam = Camera.main;
            if (cam != null && sprites.Length > 0)
            {
                SpriteRenderer biggest = null;
                float bestArea = 0f;
                foreach (var sr in sprites)
                {
                    float a = sr.bounds.size.x * sr.bounds.size.y;
                    if (a > bestArea) { bestArea = a; biggest = sr; }
                }
                if (biggest != null)
                {
                    float d = Mathf.Abs(VibeTokens.Luminance(cam.backgroundColor) - VibeTokens.Luminance(biggest.color));
                    if (d < VibeTokens.MinGroundBgLuminanceDelta)
                        Warn($"ground/bg luminance Δ {d:0.000} < {VibeTokens.MinGroundBgLuminanceDelta} — ground barely reads vs sky");
                    else Pass($"ground/bg contrast Δ {d:0.000}");
                }
            }

            // 5. Scatter / emptiness proxy.
            if (sprites.Length < VibeTokens.MinScatterProps + 3)
                Warn($"only {sprites.Length} sprites in scene — looks empty (composition floor wants ≥{VibeTokens.MinScatterProps} scatter props + actors)");
            else Pass($"{sprites.Length} sprites (scatter floor met)");

            // 5b. Audio floor: exactly one enabled AudioListener (Codely forgets it → silent sfx).
            var listeners = Object.FindObjectsOfType<AudioListener>();
            if (listeners.Length == 0) Fail("no AudioListener in scene — all sfx silent (run Flynn > VibeLint > Fix Common)");
            else if (listeners.Length > 1) Warn($"{listeners.Length} AudioListeners — Unity wants exactly one");
            else Pass("one AudioListener present");

            // 6. Sorting ambiguity: overlapping sprites on the same layer+order with no y-sort
            //    component — render order is undefined (the classic "wrong layer" bug).
            var ambiguous = new List<string>();
            for (int i = 0; i < sprites.Length && i < 200; i++)
            {
                for (int j = i + 1; j < sprites.Length && j < 200; j++)
                {
                    var a = sprites[i]; var b = sprites[j];
                    if (a.sortingLayerID != b.sortingLayerID || a.sortingOrder != b.sortingOrder) continue;
                    if (!a.bounds.Intersects(b.bounds)) continue;
                    if (HasYSort(a.gameObject) || HasYSort(b.gameObject)) continue;
                    ambiguous.Add($"{a.gameObject.name}~{b.gameObject.name} (layer {a.sortingLayerName}, order {a.sortingOrder})");
                }
            }
            if (ambiguous.Count > 0)
                Warn($"ambiguous sprite sorting — overlapping, same layer+order, no y-sort (use VibeSpec §13 bands): {string.Join("; ", ambiguous)}");
            else Pass("no ambiguous sprite sorting");

            // 7. FeedbackSO contract checks: tier bands, tier-required sfx, module placement.
            bool sfxLibraryPresent = AssetDatabase.FindAssets("t:AudioClip",
                new[] { "Assets/Flynn/Audio/Placeholder" }).Length > 0;
            var silentHigh = new List<string>();
            var offBand = new List<string>();
            var strays = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:FeedbackSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var fb = AssetDatabase.LoadAssetAtPath<FeedbackSO>(path);
                if (fb == null) continue;

                // Tier treatment bands (VibeSpec §8) — punch must sit inside its tier's band.
                float p = fb.scalePunch;
                bool ok = fb.tier switch
                {
                    FeedbackTier.Ambient => p <= 0.001f,
                    FeedbackTier.Informational => p <= 0.10f,
                    FeedbackTier.Action => p <= 0.35f,
                    FeedbackTier.Success => p >= 0.15f && p <= 0.50f,
                    FeedbackTier.Milestone => p <= 0.60f,
                    _ => true
                };
                if (!ok) offBand.Add($"{fb.name} ({fb.tier}, punch {p:0.00})");

                // Success/Milestone require sfx (FAIL once the SFX library exists, warn before).
                if (fb.sfx == null && fb.tier >= FeedbackTier.Success) silentHigh.Add($"{fb.name} ({fb.tier})");

                // Cullability: feedback assets live in their module (or shared Feel).
                if (!path.StartsWith("Assets/Flynn/Modules/") && !path.StartsWith("Assets/Flynn/Feel"))
                    strays.Add(fb.name);
            }
            if (offBand.Count > 0) Warn($"FeedbackSO outside tier treatment band (VibeSpec §8): {string.Join(", ", offBand)}");
            else Pass("all FeedbackSO within tier bands");
            if (silentHigh.Count > 0)
            {
                if (sfxLibraryPresent) Fail($"Success/Milestone feedback with no sfx (required by tier): {string.Join(", ", silentHigh)}");
                else Warn($"Success/Milestone feedback with no sfx: {string.Join(", ", silentHigh)} (becomes FAIL once Audio/Placeholder has clips)");
            }
            else Pass("tier-required sfx assigned");
            if (strays.Count > 0) Warn($"FeedbackSO outside a module folder (breaks cullability): {string.Join(", ", strays)}");
            else Pass("feedback assets live in modules");

            string verdict = fails > 0 ? "FAIL" : warns > 0 ? "PASS w/ warnings" : "PASS";
            Debug.Log($"[VibeLint] {verdict} — {fails} fail, {warns} warn\n{sb}");
        }

        /// <summary>Deterministic auto-repair for the recurring Codely omissions.</summary>
        [MenuItem("Flynn/VibeLint/Fix Common")]
        public static void FixCommon()
        {
            var fixes = new StringBuilder();

            // Missing AudioListener → put one on the main camera.
            if (Object.FindObjectsOfType<AudioListener>().Length == 0)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Undo.AddComponent<AudioListener>(cam.gameObject);
                    fixes.AppendLine("  + AudioListener added to Main Camera");
                }
                else fixes.AppendLine("  ! no Main Camera found — add a camera first");
            }

            Debug.Log(fixes.Length > 0
                ? $"[VibeLint] Fix Common applied:\n{fixes}"
                : "[VibeLint] Fix Common: nothing to fix");
        }

        /// <summary>Any component whose type name marks it as y-sort-owned (module-agnostic).</summary>
        static bool HasYSort(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n.Contains("YSort") || n.Contains("SortableSprite") || n.Contains("SortingGroup"))
                    return true;
            }
            return false;
        }
    }
}
