using UnityEngine;
using Flynn.Npc;

namespace Flynn.Modules.DialogueLLM
{
    /// Player names themself once; the NPC uses it (ownership effect × live LLM).
    /// Applies to PlayerDialogueProfile.displayName — the prompt-injection point the
    /// old stack already reads — and persists via PlayerPrefs.
    public class PlayerNameSetter : MonoBehaviour
    {
        private const string PrefsKey = "Flynn.PlayerName";

        [SerializeField] private PlayerDialogueProfile _profile;

        public string CurrentName =>
            _profile != null ? _profile.displayName : PlayerPrefs.GetString(PrefsKey, "Traveler");

        public bool HasStoredName => PlayerPrefs.HasKey(PrefsKey);

        private void Awake()
        {
            if (_profile != null && HasStoredName)
                _profile.displayName = PlayerPrefs.GetString(PrefsKey);
        }

        public void SetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string trimmed = name.Trim();
            if (trimmed.Length > 24) trimmed = trimmed.Substring(0, 24);
            if (_profile != null) _profile.displayName = trimmed;
            PlayerPrefs.SetString(PrefsKey, trimmed);
            PlayerPrefs.Save();
        }
    }
}
