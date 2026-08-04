using TMPro;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// HUD_Lab scene rig — fires every HUD feedback moment from the keyboard so each one can
    /// be tuned by feel with zero gameplay present. Module scaffolding: stays behind if the
    /// HUD graduates.
    ///
    /// P pick random item · T pick wrench · O drop from selected slot · H hold = pickup spam
    /// B drain battery · N gain battery · C toggle charging
    /// 1-6 / Q W E select · drag between slots = move/swap/merge · drag off the bars = drop
    /// </summary>
    public class HUDTester : MonoBehaviour
    {
        [Tooltip("The HUD under test.")]
        public PlayerHUD hud;
        [Tooltip("Non-tool items P cycles through.")]
        public HUDItemDef[] items;
        [Tooltip("The pinned tool — T picks it up; it always returns to its hotkey.")]
        public HUDItemDef wrench;

        [Header("Battery test steps")]
        public float drainStep = 0.15f;
        public float gainStep = 0.15f;
        [Tooltip("Seconds between spam pickups while H is held (Contract §16 check).")]
        public float spamInterval = 0.12f;

        [Header("Flight-anchor experiment (V toggles hud.anchorFlightsToPlayer)")]
        [Tooltip("Arrow keys move hud.playerAnchor at this speed, world units/sec.")]
        public float playerMoveSpeed = 4f;

        bool _charging;
        float _spamTimer;

        void Start()
        {
            if (hud == null) { Debug.LogWarning("[HUDTester] no hud assigned"); enabled = false; return; }
            hud.onItemDropped.AddListener((def, n) =>
                Debug.Log($"[HUDTester] dropped {n}x {def.displayName}"));
            hud.onToolSelected.AddListener(def =>
                Debug.Log($"[HUDTester] tool ready: {def.displayName}"));
            hud.onBatteryEmpty.AddListener(() => Debug.Log("[HUDTester] battery empty — tools rest"));
            hud.onBatteryFull.AddListener(() => Debug.Log("[HUDTester] battery full"));
            hud.onPocketsFull.AddListener(() => Debug.Log("[HUDTester] pockets full"));
            BuildHelpText();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.P)) PickRandom();
            if (Input.GetKeyDown(KeyCode.T) && wrench != null)
                hud.PickUp(wrench, Input.mousePosition);
            if (Input.GetKeyDown(KeyCode.O)) hud.DropSelected(Input.mousePosition);

            if (Input.GetKeyDown(KeyCode.B)) hud.DrainBattery(drainStep);
            if (Input.GetKeyDown(KeyCode.N)) hud.AddBattery(gainStep);
            if (Input.GetKeyDown(KeyCode.C))
            {
                _charging = !_charging;
                hud.SetCharging(_charging);
                Debug.Log($"[HUDTester] charging {(_charging ? "on" : "off")}");
            }

            // Spam check: punches must not stack runaway, sfx must aggregate.
            if (Input.GetKey(KeyCode.H))
            {
                _spamTimer -= Time.deltaTime;
                if (_spamTimer <= 0f) { _spamTimer = spamInterval; PickRandom(); }
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                hud.anchorFlightsToPlayer = !hud.anchorFlightsToPlayer;
                Debug.Log($"[HUDTester] flights anchor: " +
                    (hud.anchorFlightsToPlayer ? "player sprite" : "HUD slots"));
            }
            MovePlayer();
        }

        // Arrow keys (not WASD — W is a tool hotkey) so pickups/drops can be felt in motion.
        void MovePlayer()
        {
            if (hud.playerAnchor == null) return;
            var dir = new Vector3(
                (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f),
                0f);
            if (dir != Vector3.zero)
                hud.playerAnchor.position += dir.normalized * (playerMoveSpeed * Time.deltaTime);
        }

        void PickRandom()
        {
            if (items == null || items.Length == 0) return;
            hud.PickUp(items[Random.Range(0, items.Length)], Input.mousePosition);
        }

        void BuildHelpText()
        {
            var go = new GameObject("HelpText", typeof(RectTransform));
            go.transform.SetParent(hud.CanvasRoot, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 20f;
            tmp.color = VibeTokens.UiTextMuted;
            tmp.alignment = TextAlignmentOptions.TopRight;
            tmp.raycastTarget = false;
            tmp.text =
                "P pick item    T pick wrench    O drop selected\n" +
                "1-6 / Q W E select    drag = move / swap / merge\n" +
                "drag off the bars = drop    H hold = spam pickups\n" +
                "B drain    N gain    C charge toggle\n" +
                "V anchor flights: HUD <-> player    arrows move player";
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(640f, 130f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
        }
    }
}
