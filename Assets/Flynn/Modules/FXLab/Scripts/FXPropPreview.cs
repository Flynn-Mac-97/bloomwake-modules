using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Lets a lab prop wear stand-in art while an effect is being dialed: drop a real tree
    /// sprite into an effect's preview slot and the focus prop becomes that tree, so chip
    /// colours, squash and flash are judged against the silhouette they will really play on
    /// instead of a coloured square.
    ///
    /// The prop's own sprite, scale and collider size are captured on the FIRST swap and
    /// restored on Clear - the props stay disposable and nothing is authored away. Purely a
    /// lab affordance; no game code should need this.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FXPropPreview : MonoBehaviour
    {
        SpriteRenderer _sr;
        BoxCollider2D _box;
        Sprite _originalSprite;
        Vector3 _originalScale;
        Vector2 _originalBoxSize;
        Vector2 _originalBoxOffset;
        bool _captured;

        public bool IsPreviewing { get; private set; }

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _box = GetComponent<BoxCollider2D>();
        }

        /// <summary>Wear the slot's sprite, or restore the prop if the slot has no art.</summary>
        public void Apply(PreviewSlot slot)
        {
            if (slot == null || slot.sprite == null) { Clear(); return; }
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) return;

            Capture();
            _sr.sprite = slot.sprite;
            transform.localScale = _originalScale * Mathf.Max(0.01f, slot.scale);
            IsPreviewing = true;
            FitCollider();
            RebaseScaleOwners();
        }

        /// <summary>Back to the prop's own art. Safe to call when nothing was ever applied.</summary>
        public void Clear()
        {
            if (!_captured || !IsPreviewing) return;
            if (_sr != null) _sr.sprite = _originalSprite;
            transform.localScale = _originalScale;
            if (_box != null)
            {
                _box.size = _originalBoxSize;
                _box.offset = _originalBoxOffset;
            }
            IsPreviewing = false;
            RebaseScaleOwners();
        }

        /// <summary>
        /// Tell the other components that own localScale about the new size.
        ///
        /// Breather rewrites localScale EVERY frame from the size it captured at Awake, and
        /// SquashFX lerps from its own Awake-time base - so without this the art-scale slider
        /// appears to do nothing (the breather stomps it on the next Update) and the first
        /// squash snaps the prop back to its original size.
        /// </summary>
        void RebaseScaleOwners()
        {
            var breather = GetComponent<Flynn.Feel.Breather>();
            if (breather != null) breather.Rebase();
            var squash = GetComponent<SquashFX>();
            if (squash != null) squash.Rebase();
        }

        void Capture()
        {
            if (_captured) return;
            _originalSprite = _sr.sprite;
            _originalScale = transform.localScale;
            if (_box != null)
            {
                _originalBoxSize = _box.size;
                _originalBoxOffset = _box.offset;
            }
            _captured = true;
        }

        // click-to-focus raycasts the collider, so a swapped-in tree has to be clickable at
        // its new size - otherwise the prop you can see is not the prop you can select
        void FitCollider()
        {
            if (_box == null || _sr == null || _sr.sprite == null) return;
            var b = _sr.sprite.bounds;
            _box.size = b.size;
            _box.offset = b.center;
        }
    }
}
