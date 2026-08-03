using System;
using System.Collections.Generic;
using Flynn.Feel;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// The code-side recipe authoring surface: built-in combos shipped as factories.
    /// Each entry seeds ONCE per tuning asset (tracked via FXLabTuning.seededNames) -
    /// dialed values are never overwritten, designer deletions stay deleted, and a new
    /// entry appears in the lab's recipe list on next load. New combo = one factory
    /// method + one Builtin line; the designer re-dials it in the panel from there.
    /// </summary>
    public static class FXRecipeLibrary
    {
        public static readonly (string name, Func<FXRecipe> build)[] Builtin =
        {
            // composition: ONE swing, per-material contact, moment = the two together
            ("SwingTool", SwingTool),
            ("ContactWood", ContactWood),
            ("ContactStone", ContactStone),
            ("Hit_Wood", HitWoodComposed),
            ("Hit_Stone", HitStoneComposed),
            // the flat original, kept for A/B against the composed pair
            ("HitWood", HitWood),
            // tend
            ("Repair", Repair)

            // PARKED until the mechanic behind it exists - a recipe with no verb to answer
            // is dialing you would redo anyway. The factory below stays; re-add the line to
            // seed it into a fresh asset (already-seeded assets keep whatever they hold).
            //   ("ScanPulse", ScanPulse)
        };

        // ── composition: swing once, answer per material ─────────────────────────
        // A moment is not a flat pile of primitives. The tool swing is the SAME every time,
        // so it is authored once here and every material hit just references it; what differs
        // is the contact. Re-dial SwingTool and every hit in the game changes with it.

        /// <summary>The player's swing, on its own: body lunge + tool arc + whoosh. Owns no
        /// contact - it does not know or care what it is about to hit.</summary>
        static FXRecipe SwingTool() => new FXRecipe
        {
            name = "SwingTool",
            blocks = new List<FXBlock>
            {
                new SfxBlock { sfx = new SfxSlot { pitchJitter = 0.06f } },
                new LungeBlock(),
                new SwingArcBlock { arc = ArcVariant.Sheet }
            }
        };

        /// <summary>What wood answers with: warm flash, generous squash, chipped-wood debris,
        /// small kick. Hitstop last so the impact frame freezes at peak.</summary>
        static FXRecipe ContactWood() => new FXRecipe
        {
            name = "ContactWood",
            blocks = new List<FXBlock>
            {
                new SfxBlock { sfx = new SfxSlot { pitchJitter = 0.08f } },
                new FlashBlock { flash = new FlashSettings { color = new Color(1f, 0.9f, 0.7f) } },
                new SquashBlock { squash = new SquashSettings { punch = 0.22f } },
                new PuffBlock
                {
                    puff = new PuffSettings
                    {
                        colors = new[]
                        {
                            new Color(0.54f, 0.42f, 0.28f),   // chipped wood
                            VibeTokens.GroundAccent,          // leaf
                            VibeTokens.FoliageDeep
                        }
                    }
                },
                // weight carries no material opinion - link these so dialing the Cam Nudge /
                // Hit Stop primitives retunes every hit at once
                new NudgeBlock { usePrimitive = true },
                new HitStopBlock { usePrimitive = true }
            }
        };

        /// <summary>What stone answers with: shorter/harder everything - tight flash, small
        /// squash, grey grit, a longer freeze. Same shape as wood, different material.</summary>
        static FXRecipe ContactStone() => new FXRecipe
        {
            name = "ContactStone",
            blocks = new List<FXBlock>
            {
                new SfxBlock { sfx = new SfxSlot { pitchJitter = 0.06f } },
                new FlashBlock
                {
                    flash = new FlashSettings { color = new Color(0.95f, 0.97f, 1f), decay = 0.09f }
                },
                new SquashBlock { squash = new SquashSettings { punch = 0.12f, duration = 0.2f } },
                new PuffBlock
                {
                    puff = new PuffSettings
                    {
                        count = 10, speed = 1.3f, size = 0.035f, life = 0.45f, spinDegrees = 200f,
                        colors = new[]
                        {
                            new Color(0.62f, 0.62f, 0.64f),
                            new Color(0.45f, 0.45f, 0.48f),
                            new Color(0.78f, 0.77f, 0.74f)
                        }
                    }
                },
                new NudgeBlock { nudge = new NudgeSettings { amplitude = 0.05f } },
                new HitStopBlock { hitStop = new HitStopSettings { duration = 0.05f } }
            }
        };

        /// <summary>The whole moment as a two-line combination: swing, then the material's
        /// answer 0.14s later. The nested block's delay IS the contact delay.</summary>
        static FXRecipe HitWoodComposed() => new FXRecipe
        {
            name = "Hit_Wood",
            blocks = new List<FXBlock>
            {
                new RecipeBlock { recipe = "SwingTool" },
                new RecipeBlock { recipe = "ContactWood", delay = 0.14f }
            }
        };

        /// <summary>Same swing, different answer - the point of the whole arrangement.</summary>
        static FXRecipe HitStoneComposed() => new FXRecipe
        {
            name = "Hit_Stone",
            blocks = new List<FXBlock>
            {
                new RecipeBlock { recipe = "SwingTool" },
                new RecipeBlock { recipe = "ContactStone", delay = 0.12f }
            }
        };

        /// <summary>The typed hitWood contact stack as data - same defaults, same order,
        /// hitstop last. The A/B reference recipe for the block system.</summary>
        static FXRecipe HitWood() => new FXRecipe
        {
            name = "HitWood",
            swingOpens = true,
            blocks = new List<FXBlock>
            {
                new SfxBlock { sfx = new SfxSlot { pitchJitter = 0.08f } },
                new FlashBlock { flash = new FlashSettings { color = new Color(1f, 0.9f, 0.7f) } },
                new SquashBlock { squash = new SquashSettings { punch = 0.22f } },
                new PuffBlock
                {
                    puff = new PuffSettings
                    {
                        colors = new[]
                        {
                            new Color(0.54f, 0.42f, 0.28f),   // chipped wood
                            VibeTokens.GroundAccent,          // leaf
                            VibeTokens.FoliageDeep
                        }
                    }
                },
                new BurstBlock { enabled = false },
                new NudgeBlock(),
                new HitStopBlock()
            }
        };

        /// <summary>The anti-hit as data: soft green glow + happy bounce + motes + pulse.
        /// Deliberately no hitstop / no nudge - care, not impact.</summary>
        static FXRecipe Repair() => new FXRecipe
        {
            name = "Repair",
            blocks = new List<FXBlock>
            {
                new SfxBlock(),
                new FlashBlock
                {
                    impactFrame = false,
                    flash = new FlashSettings
                        { color = new Color(0.8f, 1f, 0.82f), peak = 0.7f, attack = 0.05f, decay = 0.3f }
                },
                new SquashBlock { squash = new SquashSettings { punch = 0.12f, duration = 0.3f } },
                new SparkleBlock { sparkle = new SparkleSettings { count = 6 }, offset = new Vector2(0f, 0.1f) },
                new RingBlock
                {
                    ring = new RingSettings
                        { color = new Color(0.71f, 0.87f, 0.54f, 0.7f), endRadius = 0.5f, duration = 0.5f }
                }
            }
        };

        /// <summary>Scan: a cool wave + brief highlight on the scanned thing. PARKED - no
        /// scanning mechanic yet; kept because it is cheap to re-seed when one lands.</summary>
        static FXRecipe ScanPulse() => new FXRecipe
        {
            name = "ScanPulse",
            blocks = new List<FXBlock>
            {
                new SfxBlock(),
                new RingBlock
                {
                    ring = new RingSettings
                    {
                        color = new Color(VibeTokens.LightCool.r, VibeTokens.LightCool.g,
                                          VibeTokens.LightCool.b, 0.8f),
                        endRadius = 0.8f, duration = 0.5f
                    }
                },
                new TintBlock
                {
                    tint = new TintStateSettings
                        { tint = VibeTokens.LightCool, blend = 0.4f, inDuration = 0.12f, autoRevert = 0.7f }
                }
            }
        };
    }
}
