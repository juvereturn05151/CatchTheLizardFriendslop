using UnityEngine;

namespace CatchTheLizard
{
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] float interactionDistance = 3f;
        NetworkPlayer player;
        PlayerHands hands;
        string prompt;

        void Awake() { player = GetComponent<NetworkPlayer>(); hands = GetComponent<PlayerHands>(); }

        void Update()
        {
            if (player == null || !player.IsOwner || Cursor.lockState != CursorLockMode.Locked) return;
            prompt = "";
            Ray ray = new(player.CameraPivot.position, player.CameraPivot.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                ContainerTool container = hit.collider.GetComponentInParent<ContainerTool>();
                if (container != null && !container.ContainsLizard.Value && hands.TryGetHeldLizard(out _, out _))
                {
                    prompt = "[E] PUT LIZARD IN CONTAINER";
                    if (Input.GetKeyDown(KeyCode.E)) hands.RequestPlaceLizard(container.NetworkObjectId);
                    return;
                }

                HoldableItem item = hit.collider.GetComponentInParent<HoldableItem>();
                if (item != null && !item.IsHeld)
                {
                    if (item is LizardController lizard)
                    {
                        if (lizard.State.Value != LizardState.Stunned) return;
                        prompt = hands.HasFreeHand ? "[E] CATCH LIZARD" : "HANDS FULL";
                    }
                    else prompt = hands.HasFreeHand ? $"[E] Pick up {item.DisplayName}" : "HANDS FULL";

                    if (hands.HasFreeHand && Input.GetKeyDown(KeyCode.E)) hands.RequestPickup(item.NetworkObjectId);
                }
            }
        }

        void OnGUI()
        {
            if (player == null || !player.IsOwner || string.IsNullOrEmpty(prompt)) return;
            GUI.Box(new Rect(Screen.width / 2f - 110, Screen.height / 2f + 45, 220, 32), prompt);
        }
    }
}
