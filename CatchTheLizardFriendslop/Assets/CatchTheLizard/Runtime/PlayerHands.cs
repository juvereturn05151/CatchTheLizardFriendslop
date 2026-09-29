using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    public sealed class PlayerHands : NetworkBehaviour
    {
        public const ulong Empty = ulong.MaxValue;
        public readonly NetworkVariable<ulong> LeftItemId = new(Empty);
        public readonly NetworkVariable<ulong> RightItemId = new(Empty);
        NetworkPlayer player;

        void Awake() => player = GetComponent<NetworkPlayer>();

        public void RequestPickup(ulong itemId) { if (IsOwner) PickupServerRpc(itemId); }
        public void RequestDrop(HandSlot slot) { if (IsOwner) DropServerRpc(slot); }
        public void RequestUse(HandSlot slot) { if (IsOwner) UseServerRpc(slot); }
        public void RequestSetUse(HandSlot slot, bool active) { if (IsOwner) SetUseServerRpc(slot, active); }
        public void RequestPlaceLizard(ulong containerId) { if (IsOwner) PlaceLizardServerRpc(containerId); }

        public bool HasFreeHand => LeftItemId.Value == Empty || RightItemId.Value == Empty;

        public bool TryGetHeldLizard(out LizardController lizard, out HandSlot slot)
        {
            if (TryGetHeldItem(HandSlot.Left, out HoldableItem left) && left is LizardController leftLizard)
            {
                lizard = leftLizard; slot = HandSlot.Left; return true;
            }
            if (TryGetHeldItem(HandSlot.Right, out HoldableItem right) && right is LizardController rightLizard)
            {
                lizard = rightLizard; slot = HandSlot.Right; return true;
            }
            lizard = null; slot = default; return false;
        }

        bool TryGetHeldItem(HandSlot slot, out HoldableItem item)
        {
            item = null;
            ulong id = slot == HandSlot.Left ? LeftItemId.Value : RightItemId.Value;
            return id != Empty && NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject obj)
                && (item = obj.GetComponent<HoldableItem>()) != null;
        }

        [ServerRpc]
        void PickupServerRpc(ulong itemId)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemId, out NetworkObject obj)) return;
            HoldableItem item = obj.GetComponent<HoldableItem>();
            if (item == null || !item.CanPickup(player) || Vector3.Distance(transform.position, item.transform.position) > item.PickupDistance) return;
            HandSlot? free = LeftItemId.Value == Empty ? HandSlot.Left : RightItemId.Value == Empty ? HandSlot.Right : null;
            if (!free.HasValue) return;
            if (free == HandSlot.Left) LeftItemId.Value = itemId; else RightItemId.Value = itemId;
            item.ServerPickup(OwnerClientId, free.Value);
        }

        [ServerRpc]
        void DropServerRpc(HandSlot slot)
        {
            SetUseInternal(slot, false);
            ulong id = slot == HandSlot.Left ? LeftItemId.Value : RightItemId.Value;
            if (id == Empty) return;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject obj))
                obj.GetComponent<HoldableItem>()?.ServerDrop(player.CameraPivot.position + player.CameraPivot.forward * 1.25f);
            if (slot == HandSlot.Left) LeftItemId.Value = Empty; else RightItemId.Value = Empty;
        }

        [ServerRpc]
        void UseServerRpc(HandSlot slot)
        {
            ulong id = slot == HandSlot.Left ? LeftItemId.Value : RightItemId.Value;
            if (id == Empty || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject obj)) return;
            if (obj.GetComponent<HoldableItem>() is IUsableItem usable) usable.ServerUse(player, slot);
        }

        [ServerRpc]
        void SetUseServerRpc(HandSlot slot, bool active) => SetUseInternal(slot, active);

        void SetUseInternal(HandSlot slot, bool active)
        {
            if (!TryGetHeldItem(slot, out HoldableItem item)) return;
            if (item is IContinuousUsableItem continuous) continuous.ServerSetUsing(player, slot, active);
            else if (active && item is IUsableItem usable) usable.ServerUse(player, slot);
        }

        [ServerRpc]
        void PlaceLizardServerRpc(ulong containerId)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(containerId, out NetworkObject obj)) return;
            ContainerTool container = obj.GetComponent<ContainerTool>();
            if (container == null || !TryGetHeldLizard(out LizardController lizard, out HandSlot slot)) return;
            if (!container.ServerTryStoreLizard(player, lizard)) return;
            if (slot == HandSlot.Left) LeftItemId.Value = Empty; else RightItemId.Value = Empty;
        }

        public void ServerResetHands()
        {
            if (!IsServer) return;
            SetUseInternal(HandSlot.Left, false);
            SetUseInternal(HandSlot.Right, false);
            LeftItemId.Value = Empty;
            RightItemId.Value = Empty;
        }
    }
}
