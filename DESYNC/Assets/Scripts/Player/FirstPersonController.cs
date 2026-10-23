using UnityEngine;
using UnityEngine.InputSystem;

namespace Desync.Player
{
    /// Starter-Assets-style FP controller: CharacterController move (no jump/sprint), mouse/stick look, footsteps.
    /// References are serialized by PlayerFactory (works when built at edit time).
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour, IResettable
    {
        public Transform head, chest;
        public Camera cam;

        const float Accel = 50f, Gravity = 20f, PitchLimit = 85f, StepInterval = 0.45f, BobAmp = 0.035f, BobFreq = 10f;

        CharacterController cc;
        Vector3 horiz, startPos;
        float vy, yaw, pitch, stepTimer, bobPhase, bobY, startYaw, sens = 1f;
        bool bob;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            PlayerRef.Root = transform;
            PlayerRef.Head = head;
            PlayerRef.Chest = chest;
            PlayerRef.Cam = cam;
            cam.fieldOfView = GameConfig.I.fov;
            startPos = transform.position;
            startYaw = yaw = transform.eulerAngles.y;
        }

        void OnDestroy()
        {
            if (PlayerRef.Root == transform) PlayerRef.Root = PlayerRef.Head = PlayerRef.Chest = null;
            if (PlayerRef.Cam == cam) PlayerRef.Cam = null;
        }

        void OnEnable() { ReadSettings(); GameEvents.SettingsChanged += ReadSettings; }
        void OnDisable() => GameEvents.SettingsChanged -= ReadSettings;
        void ReadSettings() { sens = Settings.Sensitivity; bob = Settings.HeadBob; }

        void Update()
        {
            var cfg = GameConfig.I;
            float dt = Time.deltaTime;
            bool lookFrozen = GameState.Paused || GameState.Cinematic;
            bool moveFrozen = lookFrozen || GameState.InHack || !GameState.Alive;

            if (!lookFrozen)
            {
                Vector2 look = GameInput.Look.ReadValue<Vector2>();
                // Stick binding is a per-frame rate; normalise it to 60 fps so look speed is frame-rate independent.
                if (GameInput.Look.activeControl?.device is Gamepad) look *= Time.unscaledDeltaTime * 60f;
                float k = cfg.lookSensitivity * sens;
                yaw += look.x * k;
                pitch = Mathf.Clamp(pitch - look.y * k, -PitchLimit, PitchLimit);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            Vector2 mv = moveFrozen ? Vector2.zero : Vector2.ClampMagnitude(GameInput.Move.ReadValue<Vector2>(), 1f);
            Vector3 target = (transform.right * mv.x + transform.forward * mv.y) * (cfg.moveSpeed * GameState.Mods.Clamped().MoveSpeedMult);
            horiz = Vector3.MoveTowards(horiz, target, Accel * dt);
            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy -= Gravity * dt;
            cc.Move((horiz + Vector3.up * vy) * dt);

            bool moving = cc.isGrounded && horiz.sqrMagnitude > 1f;
            if (moving)
            {
                stepTimer += dt;
                if (stepTimer >= StepInterval) { stepTimer = 0f; GameEvents.Footstep?.Invoke(); }
            }
            else stepTimer = StepInterval * 0.6f;   // first step lands ~0.2 s after starting to move

            if (lookFrozen) return;   // finale tweens the head; leave it alone
            if (bob && moving) { bobPhase += dt * BobFreq; bobY = Mathf.Sin(bobPhase) * BobAmp; }
            else bobY = Mathf.MoveTowards(bobY, 0f, dt * 0.3f);
            head.localPosition = new Vector3(0f, cfg.eyeHeight + bobY, 0f);
        }

        /// Move the feet to pos (CharacterController must be off while teleporting).
        public void Teleport(Vector3 pos) => Teleport(pos, yaw);

        public void Teleport(Vector3 pos, float newYaw)
        {
            cc.enabled = false;
            yaw = newYaw;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            cc.enabled = true;
            horiz = Vector3.zero;
            vy = 0f;
        }

        public void ResetState()
        {
            var spawn = RoomInfo.Current ? RoomInfo.Current.spawn : null;
            if (spawn) Teleport(spawn.position, spawn.eulerAngles.y);
            else Teleport(startPos, startYaw);
            pitch = bobY = 0f;
            head.localRotation = Quaternion.identity;
            head.localPosition = new Vector3(0f, GameConfig.I.eyeHeight, 0f);
        }
    }
}
