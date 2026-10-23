using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Desync.EditorSetup
{
    /// One-shot project configuration (idempotent). Run: tools/unity.sh exec setup Desync.EditorSetup.ProjectSetup.Run
    public static class ProjectSetup
    {
        public const string RpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        public const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        public static void Run()
        {
            Layers();
            CollisionMatrix();
            Rendering();
            Player();
            Assets();
            AssetDatabase.SaveAssets();
            Debug.Log("[DESYNC] ProjectSetup done");
        }

        static void Layers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 6; i < GameLayers.Names.Length; i++) layers.GetArrayElementAtIndex(i).stringValue = GameLayers.Names[i];
            tm.ApplyModifiedProperties();
        }

        static void CollisionMatrix()
        {
            int[] custom = { GameLayers.Player, GameLayers.WallRender, GameLayers.Floor, GameLayers.Ice, GameLayers.Triggers, GameLayers.Drone, GameLayers.HijackedDrone, GameLayers.Interactable };
            // Start from "custom layers collide with nothing", then allow the PRD 10.3 pairs.
            for (int a = 0; a < 32; a++) foreach (var c in custom) Physics.IgnoreLayerCollision(a, c, true);
            void Allow(int a, int b) => Physics.IgnoreLayerCollision(a, b, false);
            Allow(GameLayers.Player, GameLayers.WallRender);   // toggled at runtime by LayerState
            Allow(GameLayers.Player, GameLayers.Floor);
            Allow(GameLayers.Player, GameLayers.Drone);
            Allow(GameLayers.Player, GameLayers.Ice);
            Allow(GameLayers.Player, GameLayers.Triggers);
            Allow(GameLayers.Player, GameLayers.Default);
            Allow(GameLayers.WallRender, GameLayers.Drone);
            Allow(GameLayers.Floor, GameLayers.Default);
            foreach (var c in custom) if (c != GameLayers.Interactable) Allow(GameLayers.Floor, c);
            Allow(GameLayers.Ice, GameLayers.HijackedDrone);
            Allow(GameLayers.Default, GameLayers.Default);
        }

        static void Rendering()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpAssetPath);
            var so = new SerializedObject(rp);
            void Set(string name, System.Action<SerializedProperty> f) { var p = so.FindProperty(name); if (p != null) f(p); else Debug.LogWarning("[DESYNC] missing URP field " + name); }
            Set("m_SupportsHDR", p => p.boolValue = false);
            Set("m_MSAA", p => p.intValue = 2);
            Set("m_RenderScale", p => p.floatValue = 1f);
            Set("m_MainLightShadowsSupported", p => p.boolValue = false);
            Set("m_AdditionalLightShadowsSupported", p => p.boolValue = false);
            Set("m_AdditionalLightsRenderingMode", p => p.intValue = 1);   // per pixel
            Set("m_AdditionalLightsPerObjectLimit", p => p.intValue = 4);
            Set("m_SoftShadowsSupported", p => p.boolValue = false);
            Set("m_UseSRPBatcher", p => p.boolValue = true);
            Set("m_RequireDepthTexture", p => p.boolValue = false);
            Set("m_RequireOpaqueTexture", p => p.boolValue = false);
            so.ApplyModifiedProperties();

            GraphicsSettings.defaultRenderPipeline = rp;
            // Keep a single quality tier pointing at the tuned asset.
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            for (int i = levels.arraySize - 1; i >= 0; i--)
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue != "PC") levels.DeleteArrayElementAtIndex(i);
            var pc = levels.GetArrayElementAtIndex(0);
            pc.FindPropertyRelative("customRenderPipeline").objectReferenceValue = rp;
            pc.FindPropertyRelative("shadows").intValue = 0;
            pc.FindPropertyRelative("vSyncCount").intValue = 1;
            qs.FindProperty("m_CurrentQuality").intValue = 0;
            var perPlatform = qs.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < perPlatform.arraySize; i++) perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = 0;
            qs.ApplyModifiedProperties();
        }

        static void Player()
        {
            PlayerSettings.companyName = "BD";
            PlayerSettings.productName = "DESYNC";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Time.fixedDeltaTime = 0.02f;
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            ps.FindProperty("activeInputHandler").intValue = 1;   // Input System only
            ps.ApplyModifiedProperties();
        }

        static void Assets()
        {
            if (!AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Resources/GameConfig.asset"))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameConfig>(), "Assets/Resources/GameConfig.asset");
            AssetLib.LoadOrCreate();
        }
    }
}
