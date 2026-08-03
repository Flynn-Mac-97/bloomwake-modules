using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Code mirror of VibeSpec.md — the prototype design tokens (Lospec "Cozy Adventure" palette
    /// + size/motion floors). Use these instead of ad-hoc colors/sizes in ANY prototype code.
    /// VibeLint checks scenes against them. Final-art phase rebinds values; names stay.
    /// </summary>
    public static class VibeTokens
    {
        // ── Color tokens ─────────────────────────────────────────────────────
        public static readonly Color BgFar          = Hex(0xa2dceb);
        public static readonly Color BgDusk         = Hex(0x3b3772);
        public static readonly Color Ground         = Hex(0x6aab7c);
        public static readonly Color GroundAccent   = Hex(0xb5de89);
        public static readonly Color FoliageDeep    = Hex(0x26616b);
        public static readonly Color CharPlayer     = Hex(0xffd887);
        public static readonly Color CharNpc        = Hex(0xeb9361);
        public static readonly Color InteractHero   = Hex(0xda5e4e);
        public static readonly Color InteractPassive= Hex(0x759ed0);
        public static readonly Color Bloom          = Hex(0xe1a7c5);
        public static readonly Color Magic          = Hex(0xab7ac6);
        public static readonly Color Outline        = Hex(0x2a2140);
        public static readonly Color UiText         = Hex(0xdfffff);
        public static readonly Color LightWarm      = Hex(0xffd887);
        public static readonly Color LightCool      = Hex(0xa2dceb);

        // ── Size grammar (world units) ───────────────────────────────────────
        public const float SizePlayer       = 0.8f;
        public const float SizeSmallCritter = 0.4f;
        public const float SizePropS        = 0.3f;
        public const float SizePropM        = 0.6f;
        public const float SizePropL        = 1.2f;
        public const float SizeInteractMin  = 0.4f;

        // ── Motion floor ─────────────────────────────────────────────────────
        public const float BreatheAmplitude = 0.02f;   // 2% scale sway
        public const float BreathePeriod    = 3f;      // seconds
        public const float MinEaseDuration  = 0.15f;   // no pops

        // ── UI surface & state tokens (VibeSpec §11) ─────────────────────────
        public static readonly Color UiSurface       = new Color(0.165f, 0.129f, 0.251f, 0.92f);
        public static readonly Color UiSurfaceRaised = Hex(0x3b3772);
        public static readonly Color UiTextPrimary   = Hex(0xdfffff);
        public static readonly Color UiTextMuted     = new Color(0.875f, 1f, 1f, 0.6f);
        public static readonly Color UiAccent        = Hex(0xffd887);   // one per screen, max

        // ── Parchment UI set (VibeSpec §11b) ─────────────────────────────────
        // The player-facing skin. Values are lifted VERBATIM from the live cozy dialogue
        // stylesheet (Modules/CozyDialogue/UI/theme.uss) so the uGUI HUD and the UI Toolkit
        // dialogue cannot drift apart — same palette, two renderers. Change one, change both.
        /// <summary>Dialogue page fill — theme.uss `.dlg-panel` rgb(255,255,222).</summary>
        public static readonly Color UiParchment     = Hex(0xffffde);
        /// <summary>Raised/secondary page — theme.uss rgb(240,231,204).</summary>
        public static readonly Color UiParchmentDim  = Hex(0xf0e7cc);
        /// <summary>Deepest paper, for pinned surfaces — theme.uss rgb(236,224,194).</summary>
        public static readonly Color UiParchmentDeep = Hex(0xece0c2);
        /// <summary>Hairline rule / panel border — theme.uss rgb(201,181,131).</summary>
        public static readonly Color UiRule          = Hex(0xc9b583);
        /// <summary>Heavy rule, pinned/structural edges — theme.uss rgb(138,113,71).</summary>
        public static readonly Color UiRuleDeep      = Hex(0x8a7147);
        /// <summary>Body ink on parchment — theme.uss rgb(38,51,29).</summary>
        public static readonly Color UiInk           = Hex(0x26331d);
        /// <summary>Ink on dark chips — theme.uss rgb(231,220,190).</summary>
        public static readonly Color UiInkLight      = Hex(0xe7dcbe);
        /// <summary>Accent, one per screen — theme.uss rgb(143,191,90).</summary>
        public static readonly Color UiSprout        = Hex(0x8fbf5a);
        /// <summary>Dark nameplate chip — theme.uss rgba(42,55,32,0.86).</summary>
        public static readonly Color UiMoss          = new Color(0.165f, 0.216f, 0.125f, 0.86f);
        public const float RadiusSoftPx  = 12f;
        public const float HoverScale    = 1.05f;
        public const float PressScale    = 0.95f;
        public const float DisabledDesat = 0.5f;
        public const float ShadowAlpha   = 0.25f;   // blob shadow, outline color

        // ── Motion timing bands (VibeSpec §12) ───────────────────────────────
        public const float DurInstant  = 0.1f;
        public const float DurQuick    = 0.2f;
        public const float DurStandard = 0.4f;
        public const float DurSlow     = 0.8f;
        /// <summary>Calm-mode slot: all motion amplitudes multiply by this. Default 1.</summary>
        public static float MotionScale = 1f;

        // ── Lint thresholds ──────────────────────────────────────────────────
        public const float MinGroundBgLuminanceDelta = 0.08f;
        public const int   MinScatterProps           = 5;
        public const float MinTextContrastRatio      = 4.5f;

        public static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f, 1f);
    }
}
