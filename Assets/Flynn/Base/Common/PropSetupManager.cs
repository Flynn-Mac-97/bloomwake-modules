using System.Collections.Generic;
using UnityEngine;
using Flynn.Shadow2D;

namespace Flynn.Common
{
    /// <summary>
    /// Stamps the two markers every world sprite needs — <see cref="Shadow2DCaster"/> and
    /// <see cref="SortableSprite"/> — onto anything on the configured layers, so you never add them
    /// by hand again. Drop a sprite in the scene on a watched layer and it is rigged.
    ///
    /// This does NOT replace <see cref="Shadow2DManager"/>: that one owns the shadow children and
    /// their per-frame maths, and it already discovers casters on its own. This one only makes sure
    /// the marker is there for it to find.
    ///
    /// Edit-time work is WRITTEN INTO THE SCENE (Undo-recorded, scene marked dirty), not applied
    /// live and forgotten — GameCore is authored, not regenerated, so a rig you cannot see in the
    /// Hierarchy and cannot save is worse than none.
    ///
    /// One per scene, on MANAGERS.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PropSetupManager : MonoBehaviour
    {
        [Header("What gets rigged")]
        [Tooltip("Where to look. Empty = whole scene. Point this at your props root and everything " +
                 "under it is rigged — no per-object setup at all. This is the zero-friction path.")]
        [SerializeField] private Transform[] _roots = new Transform[0];

        [Tooltip("Also require a layer match. OFF by default: nothing in GameCore is on the " +
                 "ShadowCaster layer yet, so switching this on before you have set layers would " +
                 "silently rig nothing. Turn it on once props actually live on their own layer.")]
        [SerializeField] private bool _useLayerFilter = false;

        [Tooltip("Only used when Use Layer Filter is on.")]
        [SerializeField] private LayerMask _layers = 1 << 12;   // ShadowCaster

        [Tooltip("Names to skip. 'Shadow2D' MUST stay here — those are the shadow children " +
                 "Shadow2DManager creates, and rigging them would cast shadows of shadows.")]
        [SerializeField] private string[] _skipNames = { "Shadow2D" };

        [Header("Markers")]
        [SerializeField] private bool _addShadowCaster = true;
        [SerializeField] private bool _addSortableSprite = true;

        [Tooltip("Assigned to every SortableSprite this creates. Without it the sprite reports order " +
                 "0 and sorts under everything.")]
        [SerializeField] private SpriteSortingConfigSO _sortingConfig;

        [Header("When")]
        [Tooltip("Re-sweep whenever the Hierarchy changes in the editor. OFF by default on purpose: " +
                 "with no Roots set this sweeps the WHOLE scene, and you should choose the roots " +
                 "before it touches 83 objects. Set Roots, hit 'Rig Sprites Now', then tick this.")]
        [SerializeField] private bool _autoApplyInEditor = false;

        [Tooltip("Sweep once on Awake in play mode, catching anything spawned before this ran.")]
        [SerializeField] private bool _applyOnAwake = true;

        private static readonly List<SpriteRenderer> Buffer = new List<SpriteRenderer>();

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Application.isPlaying && _applyOnAwake) Apply();
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            UnityEditor.EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnHierarchyChanged()
        {
            if (this == null || Application.isPlaying || !_autoApplyInEditor) return;
            Apply();
        }
#endif

        // ── Sweep ──────────────────────────────────────────────────────────────

        /// <summary>Rig everything on the watched layers. Safe to call repeatedly — it only adds
        /// what is missing.</summary>
        [ContextMenu("Rig Sprites Now")]
        public void Apply()
        {
            int rigged = 0;
            foreach (var sr in Collect())
            {
                if (Rig(sr.gameObject)) rigged++;
            }

            if (rigged == 0) return;
            Debug.Log($"[PropSetup] Rigged {rigged} sprite(s).", this);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        /// <summary>Rig one object — call after spawning a prop at runtime.</summary>
        public bool Register(GameObject go) => go != null && Rig(go);

        private IEnumerable<SpriteRenderer> Collect()
        {
            Buffer.Clear();
            if (_roots != null && _roots.Length > 0)
            {
                foreach (var root in _roots)
                    if (root != null) Buffer.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true));
            }
            else
            {
                Buffer.AddRange(FindObjectsOfType<SpriteRenderer>(true));
            }
            return Buffer;
        }

        private bool Rig(GameObject go)
        {
            if (_useLayerFilter && (_layers.value & (1 << go.layer)) == 0) return false;
            if (go.GetComponent<SpriteRenderer>() == null) return false;

            foreach (var skip in _skipNames)
                if (!string.IsNullOrEmpty(skip) && go.name.Contains(skip)) return false;

            bool changed = false;
            if (_addShadowCaster && go.GetComponent<Shadow2DCaster>() == null)
            {
                AddComponent<Shadow2DCaster>(go);
                changed = true;
            }

            if (_addSortableSprite && go.GetComponent<SortableSprite>() == null)
            {
                var s = AddComponent<SortableSprite>(go);
                if (_sortingConfig != null) ApplyConfig(s);
                changed = true;
            }

            return changed;
        }

        private static T AddComponent<T>(GameObject go) where T : Component
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return UnityEditor.Undo.AddComponent<T>(go);
#endif
            return go.AddComponent<T>();
        }

        /// <summary>
        /// <c>_config</c> is a private SerializeField with no setter, so it is written through
        /// SerializedObject — which is also what records the change on a prefab instance. Setting it
        /// any other way would be dropped on save.
        /// </summary>
        private void ApplyConfig(SortableSprite s)
        {
#if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(s);
            so.FindProperty("_config").objectReferenceValue = _sortingConfig;
            so.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.EditorUtility.SetDirty(s);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(s))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(s);
#endif
        }
    }
}
