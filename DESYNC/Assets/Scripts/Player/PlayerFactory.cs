using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Desync.Player
{
    /// CONTRACT: LevelBuilder calls this once per room scene. Signature frozen; Player workstream owns the body.
    /// Must return a fully wired first-person player (CharacterController on GameLayers.Player, camera with FOV from
    /// GameConfig, AudioListener, FlipController, MeltMeter, Interactor) that registers itself in PlayerRef on Awake.
    public static class PlayerFactory
    {
        public static GameObject Create(Transform parent, Vector3 feetPos, float yaw)
        {
            var cfg = GameConfig.I;
            var go = new GameObject("Player");
            go.SetActive(false);   // wire everything before Awake runs (play mode); edit mode just serializes
            go.layer = GameLayers.Player;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(feetPos, Quaternion.Euler(0f, yaw, 0f));

            var cc = go.AddComponent<CharacterController>();
            cc.height = cfg.playerHeight;
            cc.radius = cfg.playerRadius;
            cc.center = new Vector3(0f, cfg.playerHeight * 0.5f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;

            var head = Child("Head", go.transform, cfg.eyeHeight);
            var chest = Child("Chest", go.transform, cfg.playerHeight * 0.7f);
            var camT = Child("Camera", head, 0f);
            camT.tag = "MainCamera";

            var cam = camT.gameObject.AddComponent<Camera>();
            cam.fieldOfView = cfg.fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = cfg.fogColor;
            camT.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camT.gameObject.AddComponent<AudioListener>();
            camT.gameObject.AddComponent<JuiceUtil>();

            var fpc = go.AddComponent<FirstPersonController>();
            fpc.head = head;
            fpc.chest = chest;
            fpc.cam = cam;
            go.AddComponent<MeltMeter>();
            go.AddComponent<FlipController>();
            go.AddComponent<Interactor>();

            go.SetActive(true);
            return go;
        }

        static Transform Child(string name, Transform parent, float y)
        {
            var t = new GameObject(name).transform;
            t.gameObject.layer = GameLayers.Player;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(0f, y, 0f);
            return t;
        }
    }
}
