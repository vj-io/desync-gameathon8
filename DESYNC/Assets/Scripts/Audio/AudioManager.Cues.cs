using Desync.Enemies;
using Desync.Flow;
using UnityEngine;
using RuleEvent = Desync.Rules.EventType;

namespace Desync.Audio
{
    /// M3 enemy cues played at a world position (3D, linear rolloff 2-15 m).
    public enum EnemyCue { SentinelCharge, SentinelPulse, SplitterSplit, DaemonCross, WatcherBeam }

    /// CUES for the M3 events: scoring juice, perception states, AUDITOR / TRACKER / boss stingers, the run flow (map, draft,
    /// market, cooldown) and the new enemies. Every handler is a pool play (no allocation). The results tally ticks stay in the UI.
    public sealed partial class AudioManager
    {
        const float AlertGap = 0.2f;          // one perception cue per state per 0.2 s, however many sensors change at once
        const float LanternGain = 0.4f, LanternIn = 0.3f, LanternOut = 0.5f;

        readonly float[] _alertAt = { -1e9f, -1e9f, -1e9f, -1e9f, -1e9f };
        int _tickId = -1;                     // uplink countdown tick in progress (AudioMath.UplinkTickId)
        bool _decrypt;                        // draft reveal running
        float _decT;
        int _decTicks;
        float _lanternUntil = -1f, _lanternVol;
        Vector3 _lanternPos;

        void SubscribeCues()
        {
            GameEvents.Scored += OnScored;
            GameEvents.Grazed += OnGrazed;
            EnemyEvents.TrackerSpawned += OnTrackerSpawned;
            EnemyEvents.AuditorPhase += OnAuditorPhase;
            EnemyEvents.Alerted += OnAlerted;
            EnemyEvents.SentinelCharge += OnSentinelCharge;
            EnemyEvents.SentinelPulse += OnSentinelPulse;
            EnemyEvents.SplitterSplit += OnSplitterSplit;
            EnemyEvents.WatcherSpotted += OnWatcherSpotted;
            EnemyEvents.DaemonCrossed += OnDaemonCrossed;
            EnemyEvents.AuditorDrone += OnAuditorDrone;
            EnemyEvents.PuppetString += OnPuppetString;
            BossEvents.Phase += OnBossPhase;
            BossEvents.WallSwap += OnWallSwap;
            BossEvents.StringSevered += OnStringSevered;
            BossEvents.Ending += OnBossEnding;
            RunEvents.PhaseChanged += OnRunPhase;
            RunEvents.ChooseNode += OnChooseNode;
            RunEvents.PickExploit += OnPickExploit;
            RunEvents.Buy += OnBuy;
            RunEvents.RemoveExploit += OnRemoveExploit;
            RunEvents.ChooseCooldown += OnChooseCooldown;
            RunEvents.Leave += OnUiClick;
            RunEvents.CashOut += OnUiClick;
            RunEvents.GoDeeper += OnUiClick;
            SubscribeGuards();
        }

        void UnsubscribeCues()
        {
            GameEvents.Scored -= OnScored;
            GameEvents.Grazed -= OnGrazed;
            EnemyEvents.TrackerSpawned -= OnTrackerSpawned;
            EnemyEvents.AuditorPhase -= OnAuditorPhase;
            EnemyEvents.Alerted -= OnAlerted;
            EnemyEvents.SentinelCharge -= OnSentinelCharge;
            EnemyEvents.SentinelPulse -= OnSentinelPulse;
            EnemyEvents.WatcherSpotted -= OnWatcherSpotted;
            EnemyEvents.SplitterSplit -= OnSplitterSplit;
            EnemyEvents.DaemonCrossed -= OnDaemonCrossed;
            EnemyEvents.AuditorDrone -= OnAuditorDrone;
            EnemyEvents.PuppetString -= OnPuppetString;
            BossEvents.Phase -= OnBossPhase;
            BossEvents.WallSwap -= OnWallSwap;
            BossEvents.StringSevered -= OnStringSevered;
            BossEvents.Ending -= OnBossEnding;
            RunEvents.PhaseChanged -= OnRunPhase;
            RunEvents.ChooseNode -= OnChooseNode;
            RunEvents.PickExploit -= OnPickExploit;
            RunEvents.Buy -= OnBuy;
            RunEvents.RemoveExploit -= OnRemoveExploit;
            RunEvents.ChooseCooldown -= OnChooseCooldown;
            RunEvents.Leave -= OnUiClick;
            RunEvents.CashOut -= OnUiClick;
            RunEvents.GoDeeper -= OnUiClick;
            UnsubscribeGuards();
        }

        // ------------------------------------------------------------------ scoring juice
        /// Score pops are subtle and climb one semitone per GHOST chain step (an octave at x4). REDLINE gets the slow-mo whoosh
        /// instead, and the music ducks for the 350 ms of slow motion.
        void OnScored(ScorePop s)
        {
            switch (s.type)
            {
                case RuleEvent.Redline:
                    Play2D(_bank.redlineWhoosh, 0.85f, 1f, PMax - 1);
                    _stingUntil = Time.unscaledTime + 0.45f;
                    break;
                case RuleEvent.CleanFlip:
                    Play2D(_bank.scorePop, 0.22f, AudioMath.ScorePitch(s.multiplier), PLow + 1);
                    break;
                case RuleEvent.Graze:
                case RuleEvent.Hack:
                case RuleEvent.HackPerfect:
                case RuleEvent.PuppetString:
                    Play2D(_bank.scorePop, 0.35f, AudioMath.ScorePitch(s.multiplier), PLow + 1);
                    break;
            }
        }

        /// The ICE that grazed you is the nearest one (< 1 m), so the whoosh comes from there.
        void OnGrazed(string id)
        {
            float pitch = Random.Range(0.95f, 1.05f);
            if (GameState.IceAlive > 0) Play3D(_bank.grazeWhoosh, 0.9f, pitch, PHigh, GameState.NearestIce);
            else Play2D(_bank.grazeWhoosh, 0.9f, pitch, PHigh);
        }

        // ------------------------------------------------------------------ TRACKER / AUDITOR / bosses
        void OnTrackerSpawned(Vector3 p) => Play3D(_bank.trackerSting, 1f, 1f, PHigh, p);

        /// Phase 2 = escalation, phase 3 = uplink open; 4 (uplink reached) is followed by the room's own clear cue.
        void OnAuditorPhase(int phase)
        {
            if (phase == 2) Play2D(_bank.auditorP2, 0.85f, 1f, PMax - 1);
            else if (phase == 3) Play2D(_bank.auditorP3, 0.9f, 1f, PMax - 1);
        }

        /// Uplink countdown (AUDITOR phase 3): a tick each second, twice a second under 5 s, four times under 2 s, pitch rising.
        void UpdateUplinkTicks()
        {
            var a = Auditor.Current;
            float r = a && GameState.Alive && !GameState.Paused ? a.UplinkRemaining : -1f;
            int id = AudioMath.UplinkTickId(r);
            if (id == _tickId) return;
            _tickId = id;
            if (id >= 0) Play2D(_bank.uplinkTick, 0.55f, AudioMath.UplinkPitch(r), PHigh);
        }

        void OnBossPhase(string boss, int phase) => OnAuditorPhase(phase);
        void OnWallSwap() => Play2D(_bank.wallSwap, 0.8f, 1f, PHigh);
        void OnStringSevered(int i) => Play2D(_bank.accessGranted, 0.8f, 1f + 0.1f * Mathf.Clamp(i, 0, 3), PHigh);

        void OnBossEnding(string ending)
        {
            Play2D(_bank.finaleSting, 0.9f, ending == BossEvents.Merged ? 0.85f : 1f, PMax);
            _stingUntil = Time.unscaledTime + 4f;
        }

        // ------------------------------------------------------------------ perception and the M3 enemies
        /// Sensors and hunters announce each state change; Tracing already has the TRACE alarm.
        void OnAlerted(Vector3 pos, Alert state)
        {
            int k = (int)state;
            if (k < 0 || k >= _alertAt.Length || Time.unscaledTime - _alertAt[k] < AlertGap) return;
            switch (state)
            {
                case Alert.Suspicious: _alertAt[k] = Time.unscaledTime; Play3D(_bank.alertSuspicious, 0.55f, 1f, PLow + 1, pos); break;
                case Alert.Hunting: _alertAt[k] = Time.unscaledTime; Play3D(_bank.alertHunting, 0.7f, 1f, PMid, pos); break;
                case Alert.Return: _alertAt[k] = Time.unscaledTime; Play3D(_bank.alertReturn, 0.45f, 1f, PLow + 1, pos); break;
            }
        }

        void OnSentinelCharge(Vector3 p) => PlayEnemyCue(EnemyCue.SentinelCharge, p);
        void OnSentinelPulse(Vector3 p) => PlayEnemyCue(EnemyCue.SentinelPulse, p);
        void OnSplitterSplit(Vector3 p) => PlayEnemyCue(EnemyCue.SplitterSplit, p);
        void OnWatcherSpotted(Vector3 p) => PlayEnemyCue(EnemyCue.WatcherBeam, p);
        void OnDaemonCrossed(Vector3 p, bool intoRender) => Play3D(_bank.daemonCross, 0.95f, intoRender ? 1f : 0.8f, PHigh, p);
        void OnAuditorDrone(Vector3 p, bool dispatched) { if (dispatched) Play3D(_bank.trackerSting, 0.8f, 1.3f, PHigh, p); }
        void OnPuppetString(string id) => Play2D(_bank.accessGranted, 0.7f, 1.25f, PHigh);

        /// Plays an enemy cue at a world position. Public so a hook that has no event yet (the Watcher Eye's beam) can use it.
        public void PlayEnemyCue(EnemyCue cue, Vector3 pos)
        {
            switch (cue)
            {
                case EnemyCue.SentinelCharge: Play3D(_bank.sentinelCharge, 0.8f, 1f, PMid, pos); break;
                case EnemyCue.SentinelPulse: Play3D(_bank.sentinelPulse, 1f, 1f, PHigh, pos); break;
                case EnemyCue.SplitterSplit: Play3D(_bank.splitterSplit, 0.9f, Random.Range(0.95f, 1.05f), PHigh, pos); break;
                case EnemyCue.DaemonCross: Play3D(_bank.daemonCross, 0.95f, 1f, PHigh, pos); break;
                case EnemyCue.WatcherBeam: Play3D(_bank.watcherBeam, 0.85f, 1f, PMid, pos); break;
            }
        }

        /// Call every frame while a Lantern Drone is within earshot (the nearest one): its hum follows it in RENDER, where the
        /// Lantern lives, and fades out 0.15 s after the calls stop.
        public void LanternNear(Vector3 pos)
        {
            _lanternPos = pos;
            _lanternUntil = Time.unscaledTime + 0.15f;
        }

        void UpdateLanternHum(float dt)
        {
            if (PlayerRef.Exists && LanternBeam.LanternNearest(PlayerRef.Position, out var lp) && (lp - PlayerRef.Position).sqrMagnitude < 15f * 15f) LanternNear(lp);
            bool on = Time.unscaledTime < _lanternUntil && !LayerState.InSource && GameState.Alive && !GameState.Paused && _lantern.clip;
            float target = on ? LanternGain : 0f;
            _lanternVol = Mathf.MoveTowards(_lanternVol, target, dt * LanternGain / (target > _lanternVol ? LanternIn : LanternOut));
            if (_lanternVol > 0.001f)
            {
                if (!_lantern.isPlaying) { _lantern.transform.position = _lanternPos; StartLoop(_lantern); }
                _lantern.volume = _lanternVol;
                if (on) _lantern.transform.position = _lanternPos;
            }
            else if (_lantern.isPlaying) _lantern.Stop();
        }

        // ------------------------------------------------------------------ run flow
        /// Map and Cooldown get their own cue on entry; the draft starts its decrypt ticks (a re-raise of Draft = reroll = new cards,
        /// so the reveal restarts). Any other phase ends a reveal.
        void OnRunPhase(RunPhase phase)
        {
            _decrypt = phase == RunPhase.Draft;
            _decT = 0f;
            _decTicks = 0;
            if (phase == RunPhase.Map) Play2D(_bank.mapOpen, 0.5f, 1f, PMid);
            else if (phase == RunPhase.Cooldown) Play2D(_bank.cooldown, 0.6f, 1f, PMid);
        }

        /// 16 ticks, one every 50 ms over 0.8 s, pitch rising one octave (the draft screen's reveal timing).
        void UpdateDecrypt(float dt)
        {
            if (!_decrypt) return;
            _decT += dt;
            int due = Mathf.Min(AudioMath.DecryptTicks, (int)(_decT / AudioMath.DecryptEvery));
            while (_decTicks < due) Play2D(_bank.decryptTick, 0.5f, AudioMath.DecryptPitch(_decTicks++), PMid);
            if (_decTicks >= AudioMath.DecryptTicks) _decrypt = false;
        }

        void OnChooseNode(string id) => Play2D(_bank.mapChoose, 0.6f, 1f, PHigh);

        void OnPickExploit(int index)
        {
            _decrypt = false;
            if (index >= 0) Play2D(_bank.draftPick, 0.7f, 1f, PHigh);
            else OnUiClick();   // skipped the draft
        }

        void OnBuy(int index) => Play2D(_bank.marketBuy, 0.7f, 1f, PHigh);
        void OnRemoveExploit(string id) => Play2D(_bank.marketBuy, 0.7f, 0.85f, PHigh);
        void OnChooseCooldown(int index) => Play2D(_bank.mapChoose, 0.6f, 0.85f, PHigh);
    }
}
