using UnityEngine;
using Flynn.Player;

namespace Flynn.Modules.Pouch
{
    /// LAB ONLY: keyboard add/consume + OnGUI readout so the Pouch seam is testable
    /// without HUD or Player modules. Do not ship in composed scenes.
    public class PouchLabHarness : MonoBehaviour
    {
        [SerializeField] private Pouch _pouch;
        [SerializeField] private ItemDefinition _itemA;   // suggest Wood
        [SerializeField] private ItemDefinition _itemB;   // suggest Stone
        [SerializeField] private ItemDefinition _itemC;   // suggest Biomass

        private string _lastAction = "none";
        private int _changedFires;

        private void OnEnable()
        {
            if (_pouch != null) _pouch.onChanged.AddListener(CountFire);
        }

        private void OnDisable()
        {
            if (_pouch != null) _pouch.onChanged.RemoveListener(CountFire);
        }

        private void CountFire() => _changedFires++;

        private void Update()
        {
            if (_pouch == null) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) _lastAction = $"add {Name(_itemA)}: +{_pouch.Add(_itemA)}";
            if (Input.GetKeyDown(KeyCode.Alpha2)) _lastAction = $"add {Name(_itemB)}: +{_pouch.Add(_itemB)}";
            if (Input.GetKeyDown(KeyCode.Alpha3)) _lastAction = $"add {Name(_itemC)}: +{_pouch.Add(_itemC)}";
            if (Input.GetKeyDown(KeyCode.Q)) _lastAction = $"consume {Name(_itemA)}: -{_pouch.Consume(_itemA)}";
        }

        private static string Name(ItemDefinition item) => item != null ? item.name : "null";

        private void OnGUI()
        {
            var inv = PlayerInventory.Instance;
            GUILayout.BeginArea(new Rect(10, 10, 340, 220), GUI.skin.box);
            GUILayout.Label("POUCH LAB — 1/2/3 add, Q consume A");
            if (inv == null)
            {
                GUILayout.Label("PlayerInventory.Instance = NULL");
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    var slot = inv.GetSlot(i);
                    GUILayout.Label(slot.IsEmpty ? $"slot {i}: —" : $"slot {i}: {slot.item.name} ×{slot.count}");
                }
            }
            GUILayout.Label($"counts A/B/C: {_pouch.CountOf(_itemA)}/{_pouch.CountOf(_itemB)}/{_pouch.CountOf(_itemC)}");
            GUILayout.Label($"onChanged fires: {_changedFires}");
            GUILayout.Label($"last: {_lastAction}");
            GUILayout.EndArea();
        }
    }
}
