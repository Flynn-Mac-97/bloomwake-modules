using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Tiny id-based conversation graph. Choices can require a discovered topic or a trust level
    /// (knowledge limits): unmet choices render dimmed with a gentle reason — visible, never
    /// silently hidden. Demo conversation prefilled (create asset = done).
    /// Lines may embed clickable topics as TMP links: &lt;link="topic-id"&gt;&lt;u&gt;word&lt;/u&gt;&lt;/link&gt;.
    /// </summary>
    [CreateAssetMenu(fileName = "Conversation", menuName = "Flynn/Knowledge/Conversation")]
    public class ConversationSO : ScriptableObject
    {
        [Serializable]
        public struct Choice
        {
            public string label;
            [Tooltip("Node id to jump to. Ignored when isLeave.")]
            public string nextId;
            [Tooltip("Only offered brightly once this topic is discovered ('' = always).")]
            public string requiredTopicId;
            [Tooltip("Minimum trust to offer (0 = always).")]
            public int requiredTrust;
            [Tooltip("Gentle reason shown on the dimmed row while unmet.")]
            public string lockedReason;
            public bool isLeave;
            [Tooltip("The 'key' choice — gets the copper bullet (Field Archive design).")]
            public bool isKey;
        }

        [Serializable]
        public struct Node
        {
            public string id;
            public string speaker;
            [TextArea] public string line;
            [Tooltip("Hearing this line teaches the player this topic ('' = none). Automatic knowledge update.")]
            public string discoverTopicId;
            public List<Choice> choices;
        }

        public string startNodeId = "start";
        public List<Node> nodes = new List<Node>
        {
            new Node { id = "start", speaker = "Rowan · Gardener",
                line = "Oh, hello you. The whole grove has been humming since sunrise.",
                choices = new List<Choice>
                {
                    new Choice { label = "What's humming?", nextId = "hum" },
                    new Choice { label = "About the moss lantern...", nextId = "lantern",
                        requiredTopicId = "moss-lantern", isKey = true,
                        lockedReason = "you haven't looked at it yet" },
                    new Choice { label = "Tell me about yourself.", nextId = "self",
                        requiredTrust = 1, lockedReason = "they don't know you yet" },
                    new Choice { label = "See you later.", isLeave = true },
                } },
            new Node { id = "hum", speaker = "Rowan · Gardener", discoverTopicId = "wind-chorus",
                line = "The <link=\"wind-chorus\"><u>wind chorus</u></link>. Morning wind through the hollow reeds. Lovely, isn't it?",
                choices = new List<Choice>
                {
                    new Choice { label = "Lovely indeed.", nextId = "start" },
                    new Choice { label = "See you later.", isLeave = true },
                } },
            new Node { id = "lantern", speaker = "Rowan · Gardener",
                line = "Ah, the <link=\"moss-lantern\"><u>moss lantern</u></link>. It hummed the night of the storm. The shard by the reeds remembers more than I do.",
                choices = new List<Choice>
                {
                    new Choice { label = "I'll take a look.", nextId = "start" },
                    new Choice { label = "See you later.", isLeave = true },
                } },
            new Node { id = "self", speaker = "Rowan · Gardener",
                line = "Me? I keep the reeds combed and the kettle warm. Come by any time.",
                choices = new List<Choice>
                {
                    new Choice { label = "I will.", nextId = "start" },
                    new Choice { label = "See you later.", isLeave = true },
                } },
        };

        public bool TryGetNode(string id, out Node node)
        {
            foreach (var n in nodes)
                if (n.id == id) { node = n; return true; }
            node = default;
            return false;
        }
    }
}
