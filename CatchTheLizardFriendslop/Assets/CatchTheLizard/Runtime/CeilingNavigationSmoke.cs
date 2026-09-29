#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;

namespace CatchTheLizard
{
    /// <summary>Exercises wall/ceiling navigation in the actual prototype scene.</summary>
    static class CeilingNavigationSmoke
    {
        public static IEnumerator Run()
        {
            yield return new WaitForSeconds(0.5f);
            NetworkPlayer player = Object.FindAnyObjectByType<NetworkPlayer>();
            LizardController lizard = Object.FindAnyObjectByType<LizardController>();
            GameObject ceilingObject = GameObject.Find("Ceiling");
            Collider ceiling = ceilingObject == null ? null : ceilingObject.GetComponent<Collider>();
            Collider lizardCollider = lizard == null ? null : lizard.GetComponent<Collider>();
            if (player == null || lizard == null || ceiling == null || lizardCollider == null)
            {
                Fail("Required player, lizard, or existing Ceiling collider was not found."); yield break;
            }

            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 5.1f), Quaternion.identity);
            lizard.transform.SetPositionAndRotation(new Vector3(0f, 0.24f, 6.15f), Quaternion.identity);
            lizard.State.Value = LizardState.Fleeing;
            Physics.SyncTransforms();

            float climbDeadline = Time.time + 4f;
            while (Time.time < climbDeadline &&
                   (Mathf.Abs(Vector3.Dot(lizard.SurfaceNormal.Value, Vector3.up)) > 0.4f || lizard.transform.position.y < 0.8f))
            {
                // Keep this setup deterministic even if the teleported lizard carried an
                // earlier wander timer/velocity from the scene's first startup frames.
                lizard.State.Value = LizardState.Fleeing;
                player.transform.position = new Vector3(lizard.transform.position.x, 0f, 5.1f);
                yield return null;
            }
            if (Mathf.Abs(Vector3.Dot(lizard.SurfaceNormal.Value, Vector3.up)) > 0.4f || lizard.transform.position.y < 0.8f)
            {
                Fail($"Lizard did not establish wall movement before the ceiling test. position={lizard.transform.position} player={player.transform.position} normal={lizard.SurfaceNormal.Value} state={lizard.State.Value}"); yield break;
            }

            bool reachedCeilingBoundary = false;
            bool redirectedAlongWall = false;
            bool laterMovedDown = false;
            bool penetrated = false;
            float highestY = lizard.transform.position.y;
            float highestColliderTop = lizardCollider.bounds.max.y;
            Vector3 boundaryPosition = Vector3.zero;
            float observeDeadline = Time.time + 7f;
            while (Time.time < observeDeadline)
            {
                // Inspect the rendered pose, not the previous fixed-step collider cache.
                Physics.SyncTransforms();
                if (lizardCollider.bounds.max.y > ceiling.bounds.min.y + 0.01f) penetrated = true;
                highestY = Mathf.Max(highestY, lizard.transform.position.y);
                highestColliderTop = Mathf.Max(highestColliderTop, lizardCollider.bounds.max.y);
                if (!reachedCeilingBoundary && lizardCollider.bounds.max.y >= ceiling.bounds.min.y - 0.16f)
                {
                    reachedCeilingBoundary = true;
                    boundaryPosition = lizard.transform.position;
                }
                if (reachedCeilingBoundary)
                {
                    Vector3 delta = lizard.transform.position - boundaryPosition;
                    if (Mathf.Abs(delta.x) > 0.18f || Mathf.Abs(delta.z) > 0.18f) redirectedAlongWall = true;
                    if (lizard.transform.position.y < highestY - 0.2f) laterMovedDown = true;
                }
                yield return null;
            }

            if (penetrated) { Fail($"Lizard collider entered or passed through the ceiling. ceilingBottom={ceiling.bounds.min.y:F3} highestCenter={highestY:F3} highestTop={highestColliderTop:F3} normal={lizard.SurfaceNormal.Value}"); yield break; }
            if (!reachedCeilingBoundary) { Fail("Lizard never reached the ceiling boundary."); yield break; }
            if (!redirectedAlongWall) { Fail("Lizard did not choose a sideways wall direction at the ceiling."); yield break; }
            if (Mathf.Abs(Vector3.Dot(lizard.SurfaceNormal.Value, Vector3.up)) > 0.45f)
            {
                Fail("Lizard lost its wall orientation at the ceiling boundary."); yield break;
            }
            if (!laterMovedDown) { Fail("Lizard could not redirect downward after traversing the upper wall."); yield break; }

            Debug.Log($"CTL_CEILING_OK ceilingBottom={ceiling.bounds.min.y:F3} lizardTop={lizardCollider.bounds.max.y:F3} redirected=true descended=true serverSynced={lizard.SyncedPosition.Value == lizard.transform.position}");
            EditorApplication.Exit(0);
        }

        static void Fail(string reason)
        {
            Debug.LogError("CTL_CEILING_FAIL " + reason);
            EditorApplication.Exit(6);
        }
    }
}
#endif
