using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Flynn.Feel
{
    /// <summary>
    /// Runtime knowledge state (session-only in the mockup — persistence is a later slice).
    /// Discover(id) is the single write path: it saves immediately and raises the update
    /// events (archive + alerts listen). Trust is a mock int for gating demos.
    /// Module-local events only — no bus, no singletons.
    /// </summary>
    public class KnowledgeBase : MonoBehaviour
    {
        [Tooltip("All topic definitions.")]
        public TopicLibrary library;
        [Tooltip("Mock trust level (knowledge limits demo). Tester key raises it.")]
        public int trust;

        [Tooltip("Fired with a short user-facing message ('Entry updated — Moss lantern').")]
        public UnityEvent<string> onNotify;

        /// <summary>Module-local: topic id that just became known (panels refresh on this).</summary>
        public event Action<string> TopicDiscovered;

        /// <summary>Known topic re-encountered (spoke to the NPC again, re-inspected the
        /// object). The archive uses this to keep "current" things at the top — companion
        /// recency, no notification, no unread.</summary>
        public event Action<string> TopicTouched;

        readonly HashSet<string> _known = new HashSet<string>();

        public bool IsKnown(string id) => _known.Contains(id);
        public int KnownCount => _known.Count;
        public int TotalCount => library != null ? library.topics.Count : 0;

        /// <summary>The single write path — saved immediately, announced immediately.</summary>
        public void Discover(string topicId)
        {
            if (string.IsNullOrEmpty(topicId)) return;
            if (_known.Contains(topicId)) { TopicTouched?.Invoke(topicId); return; }
            if (library == null || !library.TryGet(topicId, out var def)) return;

            _known.Add(topicId);
            onNotify?.Invoke($"Entry updated — {def.displayName}");
            TopicDiscovered?.Invoke(topicId);
        }

        public void RaiseTrust(int amount)
        {
            trust += amount;
            onNotify?.Invoke("Rowan seems more at ease with you.");
        }
    }
}
