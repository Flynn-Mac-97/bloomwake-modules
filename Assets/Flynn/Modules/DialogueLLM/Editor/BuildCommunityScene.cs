using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Flynn.Npc;
using Flynn.Npc.Memory;
using Flynn.Feel;

namespace Flynn.Modules.DialogueLLM.EditorTools
{
    /// Builds the Mosslight Clearing community demo: two LLM NPCs, a topic library whose ids
    /// match the island's signal ids exactly, and the wiring that puts what you learn into the
    /// Field Guide while you talk.
    ///
    /// The topic library is authored HERE rather than by hand so it cannot drift out of sync with
    /// mosslight_clearing.json - the signal ids and the topic ids are the same list, written once.
    /// Menu: Flynn > DialogueLLM > Build Community Scene.
    public static class BuildCommunityScene
    {
        private const string Dir = "Assets/Flynn/Modules/DialogueLLM";
        private const string ScenePath = Dir + "/Scenes/Community_Demo.unity";
        private const string TopicsPath = Dir + "/Configs/MosslightTopics.asset";
        private const string IslandPath = Dir + "/Configs/Islands/mosslight_clearing.json";

        // id -> (display, summary, lockedHint, category). ids MUST match signalId in the island file.
        private static readonly (string id, string name, string summary, string hint, TopicLibrary.Category cat)[] Topics =
        {
            ("learn.spring", "The warm spring",
             "A knee-deep pool at the centre of the clearing. It runs warm all year and has never frozen. Maren waters her seedlings from it and they come up a week early.",
             "Someone here must know why the water never freezes.", TopicLibrary.Category.World),

            ("learn.mast", "The relay mast",
             "A lattice mast on the ridge that used to carry messages between isles. Silent for a season. Bode climbs to it most mornings.",
             "There is a mast on the ridge, and it is quiet.", TopicLibrary.Category.World),

            ("learn.bees", "The bee hollow",
             "A split log at the meadow's edge that a wild colony claimed two summers ago. Maren takes only what the bees can spare, late in the day when they are slow.",
             "Something is humming at the meadow's edge.", TopicLibrary.Category.World),

            ("learn.kiln", "The old kiln",
             "A stone kiln at the north edge, cold for years. Bode is rebuilding its chimney with ridge stone because it draws better than the old brick.",
             "There is a cold kiln at the north edge.", TopicLibrary.Category.Artifact),

            ("learn.maren", "Maren",
             "Herbalist and bee-keeper of the clearing. She tends the herbs on the south bank, the bee hollow, and an orchard that never quite took.",
             "Someone here keeps the growing things.", TopicLibrary.Category.Person),

            ("learn.bode", "Bode",
             "Salvager and mender. Hinges, pumps, aerials - if it used to work and now it does not, it reaches his bench.",
             "Someone here mends what breaks.", TopicLibrary.Category.Person),

            ("learn.mast_secret", "Why the mast went silent",
             "Bode pulled the mast's feed line himself, the night it began repeating a message that was not theirs: a voice counting slowly, in no language he knew. Maren does not know.",
             "Bode goes up to the mast every morning and comes back saying nothing.", TopicLibrary.Category.Artifact),
        };

        [MenuItem("Flynn/DialogueLLM/Build Community Scene")]
        public static void Build()
        {
            var topics = BuildTopicLibrary();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.55f, 0.48f);
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            // ---- knowledge store (the Field Guide's data) ----------------------------
            var kbGo = new GameObject("Knowledge");
            var kb = kbGo.AddComponent<KnowledgeBase>();
            kb.library = topics;

            // ---- cozy UI ------------------------------------------------------------
            var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Flynn/Modules/CozyDialogue/Prefabs/CozyDialogueUI.prefab");
            GameObject uiGo = uiPrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab)
                : new GameObject("CozyDialogueUI (MISSING PREFAB)");
            uiGo.name = "FieldUI";

            /* Every cozy component takes the same KnowledgeBase through a field literally called
               "knowledge", and on the prefab they all ship as None. That unassigned reference is
               why opening the Field Guide used to throw. Assign it wherever it exists. */
            int wired = 0;
            foreach (var mb in uiGo.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var so = new SerializedObject(mb);
                var prop = so.FindProperty("knowledge");
                if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                prop.objectReferenceValue = kb;
                so.ApplyModifiedPropertiesWithoutUndo();
                wired++;
            }
            Debug.Log($"[community] assigned KnowledgeBase to {wired} cozy components");

            var field = uiGo.GetComponentInChildren<FieldDialogue>(true);
            var hud = uiGo.GetComponentInChildren<FieldArchiveHud>(true);

            // ---- the brain ----------------------------------------------------------
            var brainGo = new GameObject("LlmBrain");
            var doc = brainGo.AddComponent<UIDocument>();
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/Flynn/Modules/CozyDialogue/UI/FieldPanelSettings.asset");
            if (panelSettings != null) doc.panelSettings = panelSettings;
            doc.sortingOrder = -100;   // the brain's own panel is hidden; keep it under the cozy UI

            var llm = brainGo.AddComponent<SceneLlmManager>();
            llm.provider = LlmProvider.OpenRouter;
            llm.llmEnabled = true;
            llm.promptConfig = First<LlmPromptConfig>();
            llm.embeddingSettings = First<EmbeddingSettings>();
            llm.recalledKnowledgeChannel = First<RecalledKnowledgeChannel>();
            llm.playerProfile = First<PlayerDialogueProfile>();
            llm.sharedRemoteModelSettings = First<RemoteModelSettings>();

            var channel = First<DialogueTriggerChannel>();
            llm.triggerChannel = channel;

            var hub = brainGo.AddComponent<IslandContentHub>();
            var islandJson = AssetDatabase.LoadAssetAtPath<TextAsset>(IslandPath);
            if (islandJson == null) Debug.LogError("[community] mosslight_clearing.json not found at " + IslandPath);
            TrySet(hub, "json", islandJson);
            TrySet(hub, "islandJson", islandJson);
            llm.islandContent = hub;

            var brain = brainGo.AddComponent<DialogueManager>();
            var bso = new SerializedObject(brain);
            SetObj(bso, "uiDocument", doc);
            SetBool(bso, "_hideOwnPanel", true);
            bso.ApplyModifiedPropertiesWithoutUndo();

            var bridge = brainGo.AddComponent<LlmCozyDialogueBridge>();
            var brso = new SerializedObject(bridge);
            SetObj(brso, "field", field);
            brso.ApplyModifiedPropertiesWithoutUndo();

            var relay = brainGo.AddComponent<LlmKnowledgeRelay>();
            var rso = new SerializedObject(relay);
            SetObj(rso, "_channel", channel);
            SetObj(rso, "_knowledge", kb);
            SetObj(rso, "_hud", hud);
            rso.ApplyModifiedPropertiesWithoutUndo();

            // ---- the two NPCs -------------------------------------------------------
            var maren = MakeNpc("Maren", "maren", new Vector3(-1.6f, 0.3f, 0f),
                                new Color(0.86f, 0.78f, 0.52f), brain);
            var bode = MakeNpc("Bode", "bode", new Vector3(1.6f, 0.3f, 0f),
                               new Color(0.62f, 0.72f, 0.82f), brain);

            // ---- driver -------------------------------------------------------------
            var driverGo = new GameObject("DemoDriver");
            var driver = driverGo.AddComponent<CommunityDemoDriver>();
            var dso = new SerializedObject(driver);
            var arr = dso.FindProperty("_npcs");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = maren;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = bode;
            SetObj(dso, "_knowledge", kb);
            dso.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log("[community] built " + ScenePath);
        }

        private static NpcTalkTrigger MakeNpc(string name, string npcId, Vector3 pos, Color tint, DialogueManager brain)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Player.png");
            sr.color = tint;
            sr.sortingOrder = 5000;                     // actor band

            var link = go.AddComponent<NpcAuthoringLink>();
            TrySetString(link, "npcId", npcId);

            go.AddComponent<NpcRelationshipState>();

            var talk = go.AddComponent<NpcTalkTrigger>();
            var so = new SerializedObject(talk);
            SetObj(so, "_dialogue", brain);
            SetStr(so, "_npcId", npcId);
            SetObj(so, "_relationship", go.GetComponent<NpcRelationshipState>());
            so.ApplyModifiedPropertiesWithoutUndo();
            return talk;
        }

        private static TopicLibrary BuildTopicLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<TopicLibrary>(TopicsPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<TopicLibrary>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(TopicsPath));
                AssetDatabase.CreateAsset(lib, TopicsPath);
            }

            var list = new List<TopicLibrary.TopicDef>();
            foreach (var t in Topics)
                list.Add(new TopicLibrary.TopicDef
                {
                    id = t.id,
                    displayName = t.name,
                    summary = t.summary,
                    lockedHint = t.hint,
                    corrupted = false,
                    category = t.cat,
                });

            lib.topics = list;      // replace the SO's built-in demo defaults outright
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[community] topic library: {list.Count} topics");
            return lib;
        }

        private static T First<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            if (guids.Length == 0) { Debug.LogWarning("[community] no asset of type " + typeof(T).Name); return null; }
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void SetObj(SerializedObject so, string name, Object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[community] no field '{name}' on {so.targetObject.GetType().Name}"); return; }
            p.objectReferenceValue = value;
        }

        private static void SetStr(SerializedObject so, string name, string value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[community] no field '{name}'"); return; }
            p.stringValue = value;
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[community] no field '{name}'"); return; }
            p.boolValue = value;
        }

        private static void TrySet(Object target, string field, Object value)
        {
            if (target == null || value == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TrySetString(Object target, string field, string value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.String) return;
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
