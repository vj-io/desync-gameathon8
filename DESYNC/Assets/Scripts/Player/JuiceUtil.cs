using UnityEngine;

namespace Desync.Player
{
    /// Executes juice requests (camera shake, hit-stop) and the automatic flip/hack juice. Lives on the player camera.
    public class JuiceUtil : MonoBehaviour, IResettable
    {
        const float MaxShake = 0.12f;   // metres of camera offset at strength 1

        float shakeLeft, shakeAmp, hitStopEnd;
        bool hitStopping;

        void OnEnable()
        {
            GameEvents.ShakeRequest += Shake;
            GameEvents.HitStopRequest += HitStop;
            GameEvents.Flipped += OnFlipped;
            GameEvents.FlipRejected += OnRejected;
            GameEvents.AccessGranted += OnAccess;
        }

        void OnDisable()
        {
            GameEvents.ShakeRequest -= Shake;
            GameEvents.HitStopRequest -= HitStop;
            GameEvents.Flipped -= OnFlipped;
            GameEvents.FlipRejected -= OnRejected;
            GameEvents.AccessGranted -= OnAccess;
            EndHitStop();
        }

        void OnFlipped(bool toSource) => Shake(0.25f);

        void OnRejected()
        {
            Shake(0.7f);
            GameEvents.PopText?.Invoke("REJECTED", GameConfig.I.danger);
        }

        void OnAccess(string line)
        {
            GameEvents.HitStopRequest?.Invoke(GameConfig.I.hitStop);
            GameEvents.PopText?.Invoke("ACCESS GRANTED", GameConfig.I.cyan);
        }

        void Shake(float strength)
        {
            if (!Settings.Shake || Settings.ReduceFx) return;   // Reduce FX = shake x0
            float d = GameConfig.I.shakeDuration;
            float current = shakeLeft > 0f ? shakeAmp * shakeLeft / d : 0f;
            shakeAmp = Mathf.Max(current, Mathf.Clamp01(strength) * MaxShake);
            shakeLeft = d;
        }

        /// Paused and the death recap own Time.timeScale (both hold it at 0).
        static bool ClockHeld => GameState.Paused || GameState.Phase == GameState.PhaseKind.Recap;

        /// Freezes time for `seconds` unscaled; overlapping requests extend to the latest end. Starts only from normal time
        /// (not paused, recap, slow-mo), so ending it at 1x always restores the scale it found.
        void HitStop(float seconds)
        {
            if (ClockHeld || seconds <= 0f || (!hitStopping && Time.timeScale != 1f)) return;
            hitStopEnd = Mathf.Max(hitStopEnd, Time.unscaledTime + seconds);
            hitStopping = true;
            Time.timeScale = 0f;
        }

        void EndHitStop()
        {
            if (!hitStopping) return;
            hitStopping = false;
            if (!ClockHeld) Time.timeScale = 1f;
        }

        void Update()
        {
            if (hitStopping && Time.unscaledTime >= hitStopEnd) EndHitStop();
            if (shakeLeft <= 0f || GameState.Paused) return;
            shakeLeft -= Time.unscaledDeltaTime;
            transform.localPosition = shakeLeft > 0f
                ? Random.insideUnitSphere * (shakeAmp * shakeLeft / GameConfig.I.shakeDuration)
                : Vector3.zero;
        }

        public void ResetState()
        {
            shakeLeft = 0f;
            transform.localPosition = Vector3.zero;
            EndHitStop();
        }
    }
}
