using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public class HoldableItem : NetworkBehaviour
    {
        [SerializeField] protected string displayName = "Item";
        [SerializeField] protected Vector3 heldLocalPosition;
        [SerializeField] protected Vector3 heldLocalEuler;
        public string DisplayName => displayName;
        public readonly NetworkVariable<ulong> HolderClientId = new(PlayerHands.Empty);
        public readonly NetworkVariable<byte> HeldHand = new(0);
        public readonly NetworkVariable<Vector3> WorldPosition = new();
        public readonly NetworkVariable<Quaternion> WorldRotation = new();
        public bool IsHeld => HolderClientId.Value != PlayerHands.Empty;
        public virtual float PickupDistance => 3.2f;
        Vector3 startPosition;
        Quaternion startRotation;
        protected Collider ItemCollider { get; private set; }
        protected Rigidbody Body { get; private set; }

        protected virtual void Awake()
        {
            ItemCollider = GetComponent<Collider>();
            Body = GetComponent<Rigidbody>();
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
            // Scene items must retain their placed pose until networking has supplied
            // their initial state. Otherwise they converge on the lizard at the origin.
            if (!IsSpawned) return;

            bool held = IsHeld;
            ItemCollider.enabled = !held;
            if (Body != null) Body.isKinematic = held;
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

        public virtual bool CanPickup(NetworkPlayer player) => !IsHeld;

        public virtual void ServerPickup(ulong clientId, HandSlot hand)
        {
            if (!IsServer) return;
            HolderClientId.Value = clientId;
            HeldHand.Value = (byte)hand;
            if (Body != null) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; Body.isKinematic = true; }
        }

        public virtual void ServerDrop(Vector3 position)
        {
            if (!IsServer) return;
            HolderClientId.Value = PlayerHands.Empty;
            transform.position = position;
            if (Body != null) { Body.isKinematic = false; Body.linearVelocity = Vector3.zero; }
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
            if (Body != null) { Body.isKinematic = false; Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
        }
    }
}
