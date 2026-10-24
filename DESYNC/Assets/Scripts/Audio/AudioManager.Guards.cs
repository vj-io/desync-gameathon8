using Desync.Enemies;
using UnityEngine;

namespace Desync.Audio
{
    /// Security officer cues, all 3D at the officer (linear rolloff 2-15 m) through the shared voice pool, so the 16-voice cap
    /// holds. Their perception states already sound through EnemyEvents.Alerted ("?" suspicious, chase = hunting, return);
    /// these add the "!" sting, boots, the baton's crackle and the swing. Built from the bank's existing clips.
    public sealed partial class AudioManager
    {
        const float GuardEarshot = 15f;   // no voice is spent on a step nobody can hear
        int _guardStep;

        void SubscribeGuards()
        {
            EnemyEvents.GuardAlert += OnGuardAlert;
            EnemyEvents.GuardStep += OnGuardStep;
            EnemyEvents.GuardWindup += OnGuardWindup;
            EnemyEvents.GuardStrike += OnGuardStrike;
        }

        void UnsubscribeGuards()
        {
            EnemyEvents.GuardAlert -= OnGuardAlert;
            EnemyEvents.GuardStep -= OnGuardStep;
            EnemyEvents.GuardWindup -= OnGuardWindup;
            EnemyEvents.GuardStrike -= OnGuardStrike;
        }

        void OnGuardAlert(Vector3 p) => Play3D(_bank.trackerSting, 0.9f, 1.25f, PHigh, p);

        /// Heavier, lower boots than the player's own steps.
        void OnGuardStep(Vector3 p)
        {
            var a = _bank.footstepRender;
            if (a == null || a.Length == 0 || !PlayerRef.Exists || (p - PlayerRef.Position).sqrMagnitude > GuardEarshot * GuardEarshot) return;
            _guardStep = (_guardStep + 1) % a.Length;
            Play3D(a[_guardStep], 0.55f, Random.Range(0.76f, 0.84f), PLow, p);
        }

        /// The sentinel's charge, pitched up to fit the 0.35 s wind-up: a taser crackle.
        void OnGuardWindup(Vector3 p) => Play3D(_bank.sentinelCharge, 0.9f, 1.7f, PHigh, p);

        void OnGuardStrike(Vector3 p, bool hit) => Play3D(_bank.grazeWhoosh, hit ? 1f : 0.8f, hit ? 0.8f : 1.15f, PHigh, p);
    }
}
