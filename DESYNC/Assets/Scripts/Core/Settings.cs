using UnityEngine;

namespace Desync
{
    /// Player comfort settings (pause menu), persisted in PlayerPrefs.
    public static class Settings
    {
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sens", 1f); set => PlayerPrefs.SetFloat("sens", Mathf.Clamp(value, 0.1f, 3f)); }
        public static bool Shake { get => PlayerPrefs.GetInt("shake", 1) == 1; set => PlayerPrefs.SetInt("shake", value ? 1 : 0); }
        public static bool ReduceFlashes { get => PlayerPrefs.GetInt("flash", 0) == 1; set => PlayerPrefs.SetInt("flash", value ? 1 : 0); }
        public static bool ReduceFx { get => PlayerPrefs.GetInt("rfx", 0) == 1; set => PlayerPrefs.SetInt("rfx", value ? 1 : 0); }   // shake x0, flashes off, chromatic x0.3
        public static bool HeadBob { get => PlayerPrefs.GetInt("bob", 0) == 1; set => PlayerPrefs.SetInt("bob", value ? 1 : 0); }
        public static float ResolutionScale { get => PlayerPrefs.GetFloat("res", 1f); set => PlayerPrefs.SetFloat("res", value); }   // 0.75 or 1.0
        public static float Volume { get => PlayerPrefs.GetFloat("vol", 0.8f); set => PlayerPrefs.SetFloat("vol", Mathf.Clamp01(value)); }
    }
}
