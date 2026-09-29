using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public class HoldableItem : NetworkBehaviour
    {
        [SerializeField] string displayName = "Item";
        [SerializeField] Vector3 heldLocalPosition;
        [SerializeField] Vector3 heldLocalEuler;
        public string DisplayName => displayName;
        public readonly NetworkVariable<ulong> HolderClientId = new(PlayerHands.Empty);
        public readonly NetworkVariable<byte> HeldHand = new(0);
        public readonly NetworkVariable<Vector3> WorldPosition = new();
        public readonly NetworkVariable<Quaternion> WorldRotation = new();
        public bool IsHeld => HolderClientId.Value != PlayerHands.Empty;
        Vector3 startPosition;
        Quaternion startRotation;
        Collider itemCollider;
        Rigidbody body;

        protected virtual void Awake()
        {
            itemCollider = GetComponent<Collider>();
            body = GetComponent<Rigidbody>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                WorldPosition.Value = transform.position;
                WorldRotation.Value = transform.rotation;
            }
        }

        protected virtual void Update()
        {
            bool held = IsHeld;
            itemCollider.enabled = !held;
            if (body != null) body.isKinematic = held;
            if (held)
            {
                NetworkPlayer owner = NetworkPlayer.FindByClientId(HolderClientId.Value);
                if (owner != null)
                {
                    Transform anchor = owner.GetHandAnchor((HandSlot)HeldHand.Value);
                    transform.SetPositionAndRotation(anchor.TransformPoint(heldLocalPosition), anchor.rotation * Quaternion.Euler(heldLocalEuler));
                }
            }
            else if (!IsServer)
            {
                transform.position = Vector3.Lerp(transform.position, WorldPosition.Value, Time.deltaTime * 18f);
                transform.rotation = Quaternion.Slerp(transform.rotation, WorldRotation.Value, Time.deltaTime * 18f);
            }
            else
            {
                WorldPosition.Value = transform.position;
                WorldRotation.Value = transform.rotation;
            }
        }

        public void ServerPickup(ulong clientId, HandSlot hand)
        {
            if (!IsServer) return;
            HolderClientId.Value = clientId;
            HeldHand.Value = (byte)hand;
            if (body != null) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
        }

        public void ServerDrop(Vector3 position)
        {
            if (!IsServer) return;
            HolderClientId.Value = PlayerHands.Empty;
            transform.position = position;
            if (body != null) { body.isKinematic = false; body.linearVelocity = Vector3.zero; }
            WorldPosition.Value = position;
            WorldRotation.Value = transform.rotation;
        }

        public virtual void ServerReset()
        {
            if (!IsServer) return;
            HolderClientId.Value = PlayerHands.Empty;
            transform.SetPositionAndRotation(startPosition, startRotation);
            WorldPosition.Value = startPosition;
            WorldRotation.Value = startRotation;
            if (body != null) { body.isKinematic = false; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
    }
}
