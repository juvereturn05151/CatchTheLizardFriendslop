#if UNITY_EDITOR
using System.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace CatchTheLizard
{
    /// <summary>Command-line integration smoke for the chase/catch/container milestone.</summary>
    static class GameplayMilestoneSmoke
    {
        public static IEnumerator Run()
        {
            yield return new WaitForSeconds(0.5f);
            NetworkPlayer player = Object.FindAnyObjectByType<NetworkPlayer>();
            SprayTool spray = Object.FindAnyObjectByType<SprayTool>();
            LizardController lizard = Object.FindAnyObjectByType<LizardController>();
            ContainerTool container = Object.FindAnyObjectByType<ContainerTool>();
            ExitArea exit = Object.FindAnyObjectByType<ExitArea>();
            if (player == null || spray == null || lizard == null || container == null || exit == null)
            {
                Fail("Required scene objects did not spawn.");
                yield break;
            }

            PlayerHands hands = player.Hands;
            hands.RequestPickup(spray.NetworkObjectId);
            yield return new WaitForSeconds(0.2f);
            if (hands.LeftItemId.Value != spray.NetworkObjectId) { Fail("Spray was not picked up into the left hand."); yield break; }

            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            lizard.transform.SetPositionAndRotation(new Vector3(0f, 0.16f, 2.2f), Quaternion.identity);
            float inactiveExposure = lizard.SprayAmount.Value;
            yield return new WaitForSeconds(0.15f);
            if (spray.IsSpraying.Value || lizard.SprayAmount.Value > inactiveExposure + 0.01f)
            {
                Fail("Equipping spray activated it without hand use."); yield break;
            }

            hands.RequestSetUse(HandSlot.Left, true);
            yield return new WaitForSeconds(0.18f);
            if (!spray.IsSpraying.Value || lizard.SprayAmount.Value <= inactiveExposure)
            {
                Fail("Held left-hand use did not activate spray exposure."); yield break;
            }
            hands.RequestSetUse(HandSlot.Left, false);
            yield return new WaitForSeconds(0.1f);
            if (spray.IsSpraying.Value) { Fail("Releasing hand use did not stop spray."); yield break; }

            // Put the lizard and player in a deterministic approach to the back wall.
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 5.1f), Quaternion.identity);
            lizard.transform.SetPositionAndRotation(new Vector3(0f, 0.16f, 6.15f), Quaternion.identity);
            lizard.State.Value = LizardState.Idle;
            float wallDeadline = Time.time + 3.5f;
            while (Time.time < wallDeadline &&
                   (Vector3.Dot(lizard.SurfaceNormal.Value, Vector3.up) > 0.55f || lizard.transform.position.y < 0.8f)) yield return null;
            if (Vector3.Dot(lizard.SurfaceNormal.Value, Vector3.up) > 0.55f || lizard.transform.position.y < 0.8f)
            {
                Fail("Lizard did not transition from floor to the marked wall."); yield break;
            }

            lizard.ServerApplySpray(100f);
            yield return null;
            if (lizard.State.Value != LizardState.Falling) { Fail("Stunned wall lizard did not lose grip."); yield break; }
            float fallDeadline = Time.time + 3f;
            while (Time.time < fallDeadline && lizard.State.Value == LizardState.Falling) yield return null;
            if (lizard.State.Value != LizardState.Stunned) { Fail("Falling lizard did not land stunned."); yield break; }

            hands.RequestDrop(HandSlot.Left);
            yield return new WaitForSeconds(0.15f);
            player.transform.position = lizard.transform.position - Vector3.forward;
            hands.RequestPickup(lizard.NetworkObjectId);
            yield return new WaitForSeconds(0.2f);
            if (lizard.State.Value != LizardState.Held || hands.LeftItemId.Value != lizard.NetworkObjectId)
            {
                Fail("Empty-hand catch did not assign the lizard to the hand."); yield break;
            }

            hands.RequestDrop(HandSlot.Left);
            yield return null;
            if (lizard.State.Value != LizardState.Falling) { Fail("Dropping held lizard did not start a fall."); yield break; }
            fallDeadline = Time.time + 3f;
            while (Time.time < fallDeadline && lizard.State.Value == LizardState.Falling) yield return null;
            if (lizard.State.Value != LizardState.Stunned) { Fail("Dropped lizard did not recover to catchable stunned state."); yield break; }

            player.transform.position = lizard.transform.position - Vector3.forward;
            hands.RequestPickup(lizard.NetworkObjectId);
            yield return new WaitForSeconds(0.15f);
            if (lizard.State.Value != LizardState.Held) { Fail("Lizard could not be caught again after a drop."); yield break; }

            player.transform.position = container.transform.position - Vector3.forward;
            hands.RequestPlaceLizard(container.NetworkObjectId);
            yield return new WaitForSeconds(0.15f);
            if (!container.ContainsLizard.Value || lizard.State.Value != LizardState.Captured || hands.LeftItemId.Value != PlayerHands.Empty)
            {
                Fail("Container placement did not atomically capture and clear the hand."); yield break;
            }

            hands.RequestPickup(container.NetworkObjectId);
            yield return new WaitForSeconds(0.15f);
            if (hands.LeftItemId.Value != container.NetworkObjectId) { Fail("Occupied container could not be picked up."); yield break; }
            player.transform.position = exit.transform.position;
            yield return new WaitForSeconds(0.2f);
            if (!NetworkGameManager.Instance.TeamWon.Value) { Fail("Occupied container at exit did not trigger victory."); yield break; }

            Debug.Log("CTL_GAMEPLAY_OK spray=press-only wall=climbed fall=stunned catch=held drop=recovered container=captured victory=true");
            EditorApplication.Exit(0);
        }

        static void Fail(string reason)
        {
            Debug.LogError("CTL_GAMEPLAY_FAIL " + reason);
            EditorApplication.Exit(5);
        }
    }
}
#endif
