using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Flynn.Npc;
using Flynn.Npc.Memory;   // RecalledKnowledgeChannel
using Flynn.Feel;

namespace Flynn.Modules.DialogueLLM.EditorTools
{
    /// Builds the demo scene from scratch so it can be regenerated after any refactor, and so the
    /// wiring is readable as code rather than buried in scene YAML. Menu: Flynn > DialogueLLM >
    /// Build Demo Scene. Also runnable headless via -executeMethod.
    public static class BuildLlmDemoScene
    {
        private const string ScenePath = "Assets/Flynn/Modules/DialogueLLM/Scenes/DialogueLLM_Demo.unity";

        [MenuItem("Flynn/DialogueLLM/Build Demo Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- camera -------------------------------------------------------------
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.55f, 0.48f);   // mossy, so the parchment UI reads
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            // ---- the cozy UI (the visible surface) ------------------------------------
            var uiPrefab = Load<GameObject>("Assets/Flynn/Modules/CozyDialogue/Prefabs/CozyDialogueUI.prefab");
            GameObject uiGo = uiPrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab)
                : new GameObject("CozyDialogueUI (MISSING PREFAB)");
            uiGo.name = "FieldUI";
            var field = uiGo.GetComponentInChildren<FieldDialogue>(true);
            if (field == null) Debug.LogError("[demo] FieldDialogue not found on CozyDialogueUI prefab.");

            // ---- the brain -----------------------------------------------------------
            var brainGo = new GameObject("LlmBrain");

            /* DialogueManager.TryBindUi hard-fails without a UIDocument, even when its own panel
               is suppressed. Give it one with NO source asset: rootVisualElement still exists, the
               named-element lookups all miss, and it falls back to building the panel in C#.
               _hideOwnPanel then keeps that panel display:none. No second UXML needs shipping. */
            var doc = brainGo.AddComponent<UIDocument>();
            var panelSettings = Load<PanelSettings>("Assets/Flynn/Modules/CozyDialogue/UI/FieldPanelSettings.asset");
            if (panelSettings != null) doc.panelSettings = panelSettings;
            else Debug.LogError("[demo] FieldPanelSettings not found.");

            var llm = brainGo.AddComponent<SceneLlmManager>();
            llm.provider = LlmProvider.OpenRouter;
            llm.llmEnabled = true;
            llm.promptConfig = FindAsset<LlmPromptConfig>();
            llm.triggerChannel = FindAsset<DialogueTriggerChannel>();
            llm.embeddingSettings = FindAsset<EmbeddingSettings>();
            llm.recalledKnowledgeChannel = FindAsset<RecalledKnowledgeChannel>();
            llm.playerProfile = FindAsset<PlayerDialogueProfile>();
            llm.sharedRemoteModelSettings = FindAsset<RemoteModelSettings>();
            llm.sharedLocalModelSettings = FindAsset<LocalModelSettings>();

            var hub = brainGo.AddComponent<IslandContentHub>();
            var islandJson = Load<TextAsset>("Assets/Flynn/Modules/DialogueLLM/Configs/Islands/first_light.json");
            TrySet(hub, "json", islandJson);
            TrySet(hub, "islandJson", islandJson);
            llm.islandContent = hub;

            var brain = brainGo.AddComponent<DialogueManager>();
            var so = new SerializedObject(brain);
            SetProp(so, "uiDocument", doc);
            SetBool(so, "_hideOwnPanel", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            var bridge = brainGo.AddComponent<LlmCozyDialogueBridge>();
            var bso = new SerializedObject(bridge);
            SetProp(bso, "field", field);
            bso.ApplyModifiedPropertiesWithoutUndo();

            // ---- the NPC -------------------------------------------------------------
            var npcGo = new GameObject("NPC_Maren");
            npcGo.transform.position = new Vector3(0f, 0.4f, 0f);
            var sr = npcGo.AddComponent<SpriteRenderer>();
            sr.sprite = Load<Sprite>("Assets/Sprites/Player.png") ?? Load<Sprite>("Assets/Sprites/WhiteSquare.png");
            sr.color = new Color(0.94f, 0.85f, 0.62f);
            sr.sortingOrder = 5000;                     // actor band - see the sorting note in the README
            var talk = npcGo.AddComponent<NpcTalkTrigger>();
            var tso = new SerializedObject(talk);
            SetProp(tso, "_dialogue", brain);
            SetString(tso, "_npcId", "maren");
            tso.ApplyModifiedPropertiesWithoutUndo();

            // ---- demo driver ---------------------------------------------------------
            var driverGo = new GameObject("DemoDriver");
            var driver = driverGo.AddComponent<LlmDemoDriver>();
            var dso = new SerializedObject(driver);
            SetProp(dso, "_npc", talk);
            SetProp(dso, "_bridge", bridge);
            dso.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log("[demo] built " + ScenePath);
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        /// First asset of a type anywhere in the project. The demo only needs *a* config of each
        /// kind, and hard-coding paths would break the moment someone reorganises Configs/.
        private static T FindAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            if (guids.Length == 0) { Debug.LogWarning("[demo] no asset of type " + typeof(T).Name); return null; }
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void SetProp(SerializedObject so, string name, Object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("[demo] no serialized field '" + name + "' on " + so.targetObject.GetType().Name); return; }
            p.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("[demo] no serialized field '" + name + "'"); return; }
            p.boolValue = value;
        }

        private static void SetString(SerializedObject so, string name, string value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("[demo] no serialized field '" + name + "'"); return; }
            p.stringValue = value;
        }

        /// Field names differ between versions of IslandContentHub; set whichever exists.
        private static void TrySet(Object target, string field, Object value)
        {
            if (target == null || value == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
