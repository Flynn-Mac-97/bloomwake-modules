using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// All knowledge topics in one asset (string-id coupling — no cross-SO wiring for Codely).
    /// Demo content prefilled via field initializers: creating the asset ships the grove demo.
    /// </summary>
    [CreateAssetMenu(fileName = "TopicLibrary", menuName = "Flynn/Knowledge/Topic Library")]
    public class TopicLibrary : ScriptableObject
    {
        /// <summary>Archive category (handover §6); Threads come from TaskSO, not topics.</summary>
        public enum Category { World, Person, Artifact }

        [Serializable]
        public struct TopicDef
        {
            [Tooltip("Stable string id, referenced by conversations/inspectables/tasks.")]
            public string id;
            public string displayName;
            [TextArea] public string summary;
            [Tooltip("Shown on the dimmed codex row while undiscovered (visible unknowns).")]
            public string lockedHint;
            [Tooltip("Artifact topics read as glyph noise until decoded at the Transmitter.")]
            public bool corrupted;
            [Tooltip("Field Archive drawer category.")]
            public Category category;
        }

        public List<TopicDef> topics = new List<TopicDef>
        {
            new TopicDef { id = "rowan", displayName = "Rowan",
                summary = "Grovekeeper. Tends the mossy hollow and listens to it more than he talks.",
                lockedHint = "Someone tends this grove.", category = Category.Person },
            new TopicDef { id = "wind-chorus", displayName = "Wind chorus",
                summary = "The grove hums when the morning wind threads the hollow reeds.",
                lockedHint = "Something is humming out there.", category = Category.World },
            new TopicDef { id = "moss-lantern", displayName = "Moss lantern",
                summary = "An old lantern kept alive by luminous moss. The casing still hums faintly.",
                lockedHint = "That old lantern looks like it has a story.", category = Category.World },
            new TopicDef { id = "etched-shard", displayName = "Etched shard",
                summary = "A shard etched with tide-marks. It names the storm that woke the grove.",
                lockedHint = "Unreadable etchings. The Transmitter might help.", corrupted = true,
                category = Category.Artifact },
        };

        public bool TryGet(string id, out TopicDef def)
        {
            foreach (var t in topics)
                if (t.id == id) { def = t; return true; }
            def = default;
            return false;
        }
    }
}
