#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace CatchTheLizard
{
    // Opt-in regression for the ordinary menu -> Host Game path. No test teleport
    // occurs until the lizard has demonstrated movement from its scene position.
    public sealed class LizardMovementSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-movementtest") >= 0)
                new GameObject("Lizard movement regression").AddComponent<LizardMovementSmoke>();
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int rateArgument = Array.IndexOf(args, "-movementrate");
            int frameRate = 60;
            if (rateArgument >= 0 && rateArgument + 1 < args.Length && int.TryParse(args[rateArgument + 1], out int requestedRate))
                frameRate = Mathf.Clamp(requestedRate, 15, 240);
            Application.targetFrameRate = frameRate;
            Time.captureDeltaTime = 1f / frameRate;
            QualitySettings.vSyncCount = 0;
            LizardController lizard = FindAnyObjectByType<LizardController>();
            if (lizard == null) { Fail("No scene lizard."); yield break; }
            Vector3 placedPosition = lizard.transform.position;
            yield return new WaitForSeconds(1f);
            float menuDrift = Vector3.Distance(placedPosition, lizard.transform.position);
            Debug.Log($"CTL_MOVEMENT_MENU drift={menuDrift:F4} placed={placedPosition:F3} actual={lizard.transform.position:F3}");

            NetworkManager manager = NetworkManager.Singleton;
            manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 7788, "0.0.0.0");
            if (!manager.StartHost()) { Fail("Host did not start."); yield break; }
            Vector3 start = lizard.transform.position;
            float furthest = 0f;
            float deadline = Time.time + 2f;
            while (Time.time < deadline)
            {
                furthest = Mathf.Max(furthest, Vector3.Distance(start, lizard.transform.position));
                yield return null;
            }
            Debug.Log($"CTL_MOVEMENT_START distance={furthest:F3} position={lizard.transform.position:F3} normal={lizard.SurfaceNormal.Value:F3}");
            if (menuDrift > 0.001f) { Fail("Lizard moved before the host/client session started."); yield break; }
            if (furthest < 0.5f) { Fail("Lizard could not move from its natural spawn after hosting."); yield break; }

            // Exercise several moves before the next physics sync. Their distances must
            // accumulate even though Collider.bounds still contains the previous pose.
            lizard.ServerReset();
            lizard.transform.SetPositionAndRotation(new Vector3(0f, 0.24f, 3f), Quaternion.identity);
            Physics.SyncTransforms();
            Vector3 beforeSteps = lizard.transform.position;
            MethodInfo step = typeof(LizardController).GetMethod("TrySurfaceStep", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 4; i++)
            {
                object[] stepArgs = { Vector3.right, 0.08f, default(RaycastHit) };
                if (!(bool)step.Invoke(lizard, stepArgs)) { Fail("Unobstructed floor step was rejected."); yield break; }
            }
            float floorProgress = lizard.transform.position.x - beforeSteps.x;
            if (Mathf.Abs(floorProgress - 0.32f) > 0.01f)
            {
                Fail($"Floor steps did not accumulate: moved={floorProgress:F3} expected=0.320."); yield break;
            }

            // Reset the internal surface state as well as the pose before the wall test.
            lizard.ServerReset();
            Debug.Log($"CTL_MOVEMENT_OK menuStable=true naturalSpawnMoved=true substepDistance={floorProgress:F3} frameRate={frameRate}");
            yield return CeilingNavigationSmoke.Run();
        }

        static void Fail(string message)
        {
            Debug.LogError("CTL_MOVEMENT_FAIL " + message);
            EditorApplication.Exit(8);
        }
    }
}
#endif
