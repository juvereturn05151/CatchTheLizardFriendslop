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

        [ServerRpc]
        void PickupServerRpc(ulong itemId)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemId, out NetworkObject obj)) return;
            HoldableItem item = obj.GetComponent<HoldableItem>();
            if (item == null || item.IsHeld || Vector3.Distance(transform.position, item.transform.position) > 3.2f) return;
            HandSlot? free = LeftItemId.Value == Empty ? HandSlot.Left : RightItemId.Value == Empty ? HandSlot.Right : null;
            if (!free.HasValue) return;
            if (free == HandSlot.Left) LeftItemId.Value = itemId; else RightItemId.Value = itemId;
            item.ServerPickup(OwnerClientId, free.Value);
        }

        [ServerRpc]
        void DropServerRpc(HandSlot slot)
        {
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
    }
}
