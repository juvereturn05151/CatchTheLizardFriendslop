#if UNITY_EDITOR
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CatchTheLizard.Editor
{
    public static class PrototypeBuilder
    {
        const string Root = "Assets/CatchTheLizard";
        const string ScenePath = Root + "/Scenes/PrototypeRoom.unity";
        const string PlayerPath = Root + "/Prefabs/NetworkPlayer.prefab";

        [MenuItem("Catch the Lizard/Rebuild Playable Prototype")]
        public static void BuildPrototype()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Prefabs");
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            Material floorMat = Material("Floor", new Color(0.32f, 0.22f, 0.14f));
            Material wallMat = Material("Walls", new Color(0.78f, 0.76f, 0.66f));
            Material trimMat = Material("Trim", new Color(0.18f, 0.12f, 0.08f));
            Material sofaMat = Material("Sofa", new Color(0.18f, 0.36f, 0.40f));
            Material playerMat = Material("Player", new Color(0.95f, 0.57f, 0.15f));
            Material lizardMat = Material("Lizard", new Color(0.16f, 0.72f, 0.18f));
            Material broomMat = Material("Broom", new Color(0.66f, 0.42f, 0.18f));
            Material sprayMat = Material("Spray", new Color(0.9f, 0.25f, 0.2f));
            Material containerMat = Material("Container", new Color(0.25f, 0.72f, 0.95f));

            GameObject playerPrefab = BuildPlayerPrefab(playerMat);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject systems = new("Network Systems");
            NetworkManager manager = systems.AddComponent<NetworkManager>();
            UnityTransport transport = systems.AddComponent<UnityTransport>();
            GameObject gameState = new("Authoritative Game State");
            gameState.AddComponent<NetworkObject>();
            gameState.AddComponent<NetworkGameManager>();
            manager.NetworkConfig = new NetworkConfig { PlayerPrefab = playerPrefab, EnableSceneManagement = true, ConnectionApproval = true };
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });

            BuildLighting();
            BuildRoom(floorMat, wallMat, trimMat, sofaMat);
            BuildLizard(lizardMat);
            BuildBroom(new Vector3(-3.3f, 0.7f, -1.2f), broomMat);
            BuildSpray(new Vector3(0f, 0.65f, -0.4f), sprayMat);
            BuildContainer(new Vector3(3f, 0.55f, -1f), containerMat);
            BuildExit();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Catch the Lizard prototype generated successfully at " + ScenePath);
        }

        [MenuItem("Catch the Lizard/Build Windows Test Player")]
        public static void BuildWindowsPlayer()
        {
            if (!File.Exists(ScenePath)) BuildPrototype();
            Directory.CreateDirectory("Builds/WindowsRelease");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/WindowsRelease/CatchTheLizard.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CleanBuildCache | BuildOptions.StrictMode | BuildOptions.CompressWithLz4
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("Windows build: " + report.summary.result + " — " + report.summary.outputPath);
        }

        public static void RunEditorSmoke()
        {
            EditorSceneManager.OpenScene(ScenePath);
            double deadline = EditorApplication.timeSinceStartup + 20d;
            Application.LogCallback callback = null;
            callback = (condition, stackTrace, type) =>
            {
                if (!condition.Contains("CTL_SMOKE_OK")) return;
                Application.logMessageReceived -= callback;
                EditorApplication.Exit(condition.Contains("listening=True") ? 0 : 2);
            };
            Application.logMessageReceived += callback;
            EditorApplication.update += Timeout;
            EditorApplication.isPlaying = true;

            void Timeout()
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= Timeout;
                Application.logMessageReceived -= callback;
                Debug.LogError("CTL_SMOKE_TIMEOUT");
                EditorApplication.Exit(3);
            }
        }

        static GameObject BuildPlayerPrefab(Material mat)
        {
            GameObject root = new("NetworkPlayer");
            root.AddComponent<NetworkObject>();
            CharacterController cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.34f; cc.center = Vector3.up * 0.9f; cc.stepOffset = 0.3f;
            PlayerHands hands = root.AddComponent<PlayerHands>();
            PlayerInteraction interaction = root.AddComponent<PlayerInteraction>();
            NetworkPlayer player = root.AddComponent<NetworkPlayer>();

            GameObject torso = Primitive("Body", PrimitiveType.Capsule, root.transform, new Vector3(0, 0.9f, 0), new Vector3(0.65f, 0.9f, 0.42f), mat, false);
            GameObject head = Primitive("Head", PrimitiveType.Sphere, root.transform, new Vector3(0, 1.65f, 0), Vector3.one * 0.42f, mat, false);
            Primitive("LeftArm", PrimitiveType.Capsule, root.transform, new Vector3(-0.48f, 1.1f, 0.12f), new Vector3(0.18f, 0.55f, 0.18f), mat, false);
            Primitive("RightArm", PrimitiveType.Capsule, root.transform, new Vector3(0.48f, 1.1f, 0.12f), new Vector3(0.18f, 0.55f, 0.18f), mat, false);

            GameObject cameraPivot = new("FirstPersonCamera"); cameraPivot.transform.SetParent(root.transform); cameraPivot.transform.localPosition = Vector3.up * 1.62f;
            Camera cam = cameraPivot.AddComponent<Camera>(); cam.fieldOfView = 75f; cam.nearClipPlane = 0.05f; cameraPivot.AddComponent<AudioListener>(); cameraPivot.tag = "MainCamera";
            GameObject left = new("LeftHandAnchor"); left.transform.SetParent(cameraPivot.transform); left.transform.localPosition = new Vector3(-0.28f, -0.24f, 0.58f);
            GameObject right = new("RightHandAnchor"); right.transform.SetParent(cameraPivot.transform); right.transform.localPosition = new Vector3(0.28f, -0.24f, 0.58f);
            Primitive("LeftHand", PrimitiveType.Cube, left.transform, Vector3.zero, new Vector3(0.15f, 0.12f, 0.28f), mat, false);
            Primitive("RightHand", PrimitiveType.Cube, right.transform, Vector3.zero, new Vector3(0.15f, 0.12f, 0.28f), mat, false);

            SerializedObject so = new(player);
            so.FindProperty("cameraPivot").objectReferenceValue = cameraPivot.transform;
            so.FindProperty("leftHandAnchor").objectReferenceValue = left.transform;
            so.FindProperty("rightHandAnchor").objectReferenceValue = right.transform;
            SetArray(so.FindProperty("hideForOwner"), torso.GetComponent<Renderer>(), head.GetComponent<Renderer>());
            so.ApplyModifiedPropertiesWithoutUndo();
            _ = hands; _ = interaction;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void BuildLighting()
        {
            GameObject lightGo = new("Sun");
            Light light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.15f; light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.67f, 0.75f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.14f, 0.12f);
        }

        static void BuildRoom(Material floor, Material wall, Material trim, Material sofa)
        {
            GameObject room = new("Prototype House Room");
            ClimbablePrimitive("Floor", room.transform, new Vector3(0, -0.15f, 0), new Vector3(18, 0.3f, 14), floor);
            ClimbablePrimitive("BackWall", room.transform, new Vector3(0, 2f, 7f), new Vector3(18, 4, 0.25f), wall);
            ClimbablePrimitive("LeftWall", room.transform, new Vector3(-9f, 2f, 0), new Vector3(0.25f, 4, 14), wall);
            ClimbablePrimitive("RightWall", room.transform, new Vector3(9f, 2f, 0), new Vector3(0.25f, 4, 14), wall);
            ClimbablePrimitive("FrontWallL", room.transform, new Vector3(-5.5f, 2f, -7f), new Vector3(7, 4, 0.25f), wall);
            ClimbablePrimitive("FrontWallR", room.transform, new Vector3(5.5f, 2f, -7f), new Vector3(7, 4, 0.25f), wall);
            Primitive("DoorFrameTop", PrimitiveType.Cube, room.transform, new Vector3(0, 3.45f, -7f), new Vector3(4, 1.1f, 0.35f), trim);
            Primitive("TableTop", PrimitiveType.Cube, room.transform, new Vector3(-2.7f, 1f, 1.7f), new Vector3(3.2f, 0.22f, 1.7f), trim);
            foreach (Vector3 p in new[] { new Vector3(-4.05f,.45f,1.05f), new Vector3(-1.35f,.45f,1.05f), new Vector3(-4.05f,.45f,2.35f), new Vector3(-1.35f,.45f,2.35f) })
                Primitive("TableLeg", PrimitiveType.Cube, room.transform, p, new Vector3(.18f,.9f,.18f), trim);
            Primitive("SofaBase", PrimitiveType.Cube, room.transform, new Vector3(4.9f, .5f, 3.9f), new Vector3(3.8f, .65f, 1.7f), sofa);
            Primitive("SofaBack", PrimitiveType.Cube, room.transform, new Vector3(4.9f, 1.25f, 4.55f), new Vector3(3.8f, 1.4f, .35f), sofa);
            Primitive("Cabinet", PrimitiveType.Cube, room.transform, new Vector3(-6.7f, 1.1f, 4.8f), new Vector3(2.1f, 2.2f, 1.1f), trim);
            GameObject hide1 = new("HidePoint_Table"); hide1.transform.position = new Vector3(-2.7f, .16f, 1.7f); hide1.AddComponent<LizardHidePoint>();
            GameObject hide2 = new("HidePoint_Sofa"); hide2.transform.position = new Vector3(4.9f, .16f, 3.4f); hide2.AddComponent<LizardHidePoint>();
            for (int i = 0; i < 5; i++)
            {
                GameObject prop = Primitive("MovableProp_" + i, i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere, room.transform, new Vector3(-5 + i * 2.1f, .3f, -3.5f + (i % 2)), Vector3.one * .55f, i % 2 == 0 ? sofa : trim);
                Rigidbody rb = prop.AddComponent<Rigidbody>(); rb.mass = .6f;
            }
        }

        static void BuildLizard(Material mat)
        {
            GameObject root = new("Lizard"); root.transform.position = new Vector3(0, .16f, 3f);
            root.AddComponent<NetworkObject>();
            CapsuleCollider col = root.AddComponent<CapsuleCollider>(); col.direction = 2; col.radius = .22f; col.height = 1.05f; col.center = new Vector3(0,0,.15f);
            LizardController lizard = root.AddComponent<LizardController>();
            GameObject body = Primitive("Body", PrimitiveType.Capsule, root.transform, Vector3.zero, new Vector3(.42f,.22f,.8f), mat, false); body.transform.localRotation = Quaternion.Euler(90,0,0);
            GameObject head = Primitive("Head", PrimitiveType.Sphere, root.transform, new Vector3(0,.03f,.48f), new Vector3(.38f,.24f,.42f), mat, false);
            GameObject tail = Primitive("Tail", PrimitiveType.Capsule, root.transform, new Vector3(0,0,-.62f), new Vector3(.16f,.16f,.7f), mat, false); tail.transform.localRotation = Quaternion.Euler(90,0,0);
            for (int i=0;i<4;i++) { float side = i%2==0?-1:1; float z=i<2?.25f:-.25f; GameObject leg=Primitive("Leg",PrimitiveType.Cube,root.transform,new Vector3(side*.27f,-.04f,z),new Vector3(.32f,.06f,.08f),mat,false); leg.transform.localRotation=Quaternion.Euler(0,side*25f,0); }
            SerializedObject so = new(lizard); SetArray(so.FindProperty("visuals"), root.GetComponentsInChildren<Renderer>()); so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureItem(lizard, "Lizard", new Vector3(0,-.04f,.08f), new Vector3(0,90,0));
        }

        static void BuildBroom(Vector3 position, Material mat)
        {
            GameObject root = ToolRoot("Broom", position, new Vector3(.3f,1.45f,.3f));
            BroomTool tool = root.AddComponent<BroomTool>();
            Primitive("Handle", PrimitiveType.Cylinder, root.transform, new Vector3(0,.55f,0), new Vector3(.08f,.75f,.08f), mat, false);
            Primitive("Brush", PrimitiveType.Cube, root.transform, new Vector3(0,-.22f,.12f), new Vector3(.78f,.22f,.25f), mat, false);
            ConfigureItem(tool, "Broom", new Vector3(0,-.15f,.3f), new Vector3(8,0,-18));
        }

        static void BuildSpray(Vector3 position, Material mat)
        {
            GameObject root = ToolRoot("Spray Bottle", position, new Vector3(.45f,.7f,.35f));
            SprayTool tool = root.AddComponent<SprayTool>();
            Primitive("Bottle", PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(.38f,.62f,.3f), mat, false);
            Primitive("Nozzle", PrimitiveType.Cube, root.transform, new Vector3(0,.38f,.13f), new Vector3(.22f,.16f,.38f), mat, false);
            GameObject particles = new("SprayMist"); particles.transform.SetParent(root.transform); particles.transform.localPosition = new Vector3(0,.38f,.36f);
            ParticleSystem ps = particles.AddComponent<ParticleSystem>(); var main=ps.main; main.startLifetime=.18f; main.startSpeed=5f; main.startSize=.08f; main.startColor=new Color(.7f,.9f,1f,.55f); var emission=ps.emission; emission.rateOverTime=80; var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=14; ps.Stop();
            SerializedObject so = new(tool); so.FindProperty("sprayParticles").objectReferenceValue=ps; so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureItem(tool, "Spray", new Vector3(0,-.05f,.2f), Vector3.zero);
        }

        static void BuildContainer(Vector3 position, Material mat)
        {
            GameObject root = ToolRoot("Container", position, new Vector3(.9f,.45f,.9f));
            ContainerTool tool = root.AddComponent<ContainerTool>();
            GameObject bowl = Primitive("ContainerBody", PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(.85f,.4f,.85f), mat, false);
            Primitive("Rim", PrimitiveType.Cube, root.transform, new Vector3(0,.24f,0), new Vector3(1f,.08f,1f), mat, false);
            SerializedObject so = new(tool); so.FindProperty("indicator").objectReferenceValue=bowl.GetComponent<Renderer>(); so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureItem(tool, "Container", new Vector3(0,-.12f,.35f), new Vector3(0,0,0));
        }

        static GameObject ToolRoot(string name, Vector3 position, Vector3 colliderSize)
        {
            GameObject root = new(name); root.transform.position = position; root.AddComponent<NetworkObject>();
            BoxCollider col = root.AddComponent<BoxCollider>(); col.size = colliderSize;
            Rigidbody rb = root.AddComponent<Rigidbody>(); rb.mass = .8f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            return root;
        }

        static void ConfigureItem(HoldableItem item, string label, Vector3 heldPos, Vector3 heldEuler)
        {
            SerializedObject so = new(item); so.FindProperty("displayName").stringValue=label; so.FindProperty("heldLocalPosition").vector3Value=heldPos; so.FindProperty("heldLocalEuler").vector3Value=heldEuler; so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildExit()
        {
            GameObject exit = new("EXIT AREA — TAKE LIZARD OUTSIDE"); exit.transform.position = new Vector3(0,1,-8f); ExitArea area=exit.AddComponent<ExitArea>(); area.Size=new Vector3(3.5f,2.5f,2.5f);
        }

        static Material Material(string name, Color color)
        {
            string path=$"{Root}/Materials/{name}.mat"; Material mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat==null) { Shader shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); mat=new Material(shader); AssetDatabase.CreateAsset(mat,path); }
            mat.color=color; EditorUtility.SetDirty(mat); return mat;
        }

        static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider=true)
        {
            GameObject go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent); go.transform.localPosition=position; go.transform.localScale=scale;
            if (material!=null) go.GetComponent<Renderer>().sharedMaterial=material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject ClimbablePrimitive(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = Primitive(name, PrimitiveType.Cube, parent, position, scale, material);
            go.AddComponent<LizardClimbableSurface>();
            return go;
        }

        static void SetArray(SerializedProperty property, params Object[] objects)
        {
            property.arraySize=objects.Length; for(int i=0;i<objects.Length;i++) property.GetArrayElementAtIndex(i).objectReferenceValue=objects[i];
        }
    }
}
#endif
