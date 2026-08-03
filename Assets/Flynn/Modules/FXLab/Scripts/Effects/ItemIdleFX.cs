using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Ground-loot idle loop: item bobs on a slow sine over a fixed ground shadow that
    /// breathes inversely (item up = shadow smaller + lighter) - the shadow is what makes
    /// the float read as "on the ground". Periodic white glint winks for attention; an
    /// optional white glow pulses behind the sprite. Spawned by the Item Idle button and
    /// by ItemDropFX's settle handoff; one driver updates all live items (no per-item
    /// components).
    /// </summary>
    public class ItemIdleFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        class Item
        {
            public GameObject root;
            public Transform sprite;
            public SpriteRenderer shadowSr;
            public SpriteRenderer glowSr;
            public float phase;
            public float glintAt;
        }

        readonly List<Item> _items = new List<Item>();

        public bool HasItems => _items.Count > 0;

        /// <summary>Lab button behavior: spawn one, or clear the lot if any exist.</summary>
        public void Toggle(Vector3 groundPos)
        {
            if (_items.Count > 0) Clear();
            else Spawn(groundPos);
        }

        public void Spawn(Vector3 groundPos)
        {
            if (tuning == null) return;
            var s = tuning.idle;
            var d = tuning.drop;   // item look comes from the drop block - one life-cycle, one look

            var root = new GameObject("IdleItem");
            root.transform.position = groundPos;

            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(root.transform, false);
            var shadowSr = shadowGo.AddComponent<SpriteRenderer>();
            shadowSr.sprite = FXSprites.SoftCircle;
            shadowSr.color = new Color(0f, 0f, 0f, s.shadowAlpha);
            shadowSr.sortingOrder = 29;

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(root.transform, false);
            spriteGo.transform.localScale = Vector3.one * d.itemSize;
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            sr.sprite = d.itemSprite != null ? d.itemSprite : FXSprites.Square;
            sr.color = d.itemTint;
            sr.sortingOrder = 31;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(spriteGo.transform, false);
            glowGo.transform.localScale = Vector3.one * s.glowScale;
            var glowSr = glowGo.AddComponent<SpriteRenderer>();
            glowSr.sprite = FXSprites.SoftCircle;
            glowSr.sortingOrder = 30;
            glowSr.enabled = s.glow;

            _items.Add(new Item
            {
                root = root,
                sprite = spriteGo.transform,
                shadowSr = shadowSr,
                glowSr = glowSr,
                phase = Random.value * Mathf.PI * 2f,   // never in sync across a pile
                glintAt = Time.time + Random.value * s.glintInterval
            });
        }

        public void Clear()
        {
            foreach (var it in _items)
                if (it.root != null) Destroy(it.root);
            _items.Clear();
        }

        void Update()
        {
            if (tuning == null || _items.Count == 0) return;
            var s = tuning.idle;
            float t = Time.time;
            float twoPi = Mathf.PI * 2f;

            foreach (var it in _items)
            {
                if (it.root == null) continue;

                float w = Mathf.Sin(t / Mathf.Max(0.05f, s.bobPeriod) * twoPi + it.phase);   // -1..1
                float rise = (w + 1f) * 0.5f;                                                 // 0..1

                it.sprite.localPosition = new Vector3(
                    0f, s.hoverHeight + w * s.bobAmplitude * Flynn.Feel.VibeTokens.MotionScale, 0f);
                it.sprite.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.Sin(t / Mathf.Max(0.05f, s.bobPeriod) * twoPi * 0.7f + it.phase) * s.swayDegrees);

                // the anchor: shadow shrinks + lightens as the item rises
                float shrink = 1f - s.shadowBreathe * rise;
                it.shadowSr.transform.localScale =
                    new Vector3(s.shadowSize * shrink, s.shadowSize * s.shadowSquish * shrink, 1f);
                it.shadowSr.color = new Color(0f, 0f, 0f, s.shadowAlpha * (1f - 0.5f * s.shadowBreathe * rise));

                if (it.glowSr != null)
                {
                    it.glowSr.enabled = s.glow;
                    if (s.glow)
                    {
                        float pulse = 0.75f + 0.25f * Mathf.Sin(t / Mathf.Max(0.05f, s.glowPulsePeriod) * twoPi + it.phase);
                        it.glowSr.color = new Color(s.glowColor.r, s.glowColor.g, s.glowColor.b, s.glowAlpha * pulse);
                        it.glowSr.transform.localScale = Vector3.one * s.glowScale;
                    }
                }

                if (s.glint && t >= it.glintAt)
                {
                    it.glintAt = t + s.glintInterval * Random.Range(0.8f, 1.3f);
                    SpawnGlint(it.sprite.position, s);
                }
            }

            _items.RemoveAll(i => i.root == null);
        }

        void SpawnGlint(Vector3 pos, IdleSettings s)
        {
            var go = new GameObject("Glint");
            go.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.06f) + Vector3.up * 0.03f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = FXSprites.SoftCircle;
            sr.sortingOrder = 32;

            Vector3 start = go.transform.position;
            Tween.Custom(0f, 1f, 0.35f, onValueChange: gt =>
            {
                if (go == null) return;
                float pop = Mathf.Sin(gt * Mathf.PI);
                go.transform.localScale = Vector3.one * (0.08f * pop);
                go.transform.position = start + Vector3.up * (gt * 0.04f);
                sr.color = new Color(s.glintColor.r, s.glintColor.g, s.glintColor.b, s.glintColor.a * pop);
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }
    }
}
