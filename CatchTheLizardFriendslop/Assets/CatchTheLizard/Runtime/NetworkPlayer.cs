using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerHands), typeof(PlayerInteraction))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        static readonly Dictionary<ulong, NetworkPlayer> Players = new();
        public static NetworkPlayer FindByClientId(ulong id) => Players.TryGetValue(id, out var p) ? p : null;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 4.2f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float crouchSpeed = 2.3f;
        [SerializeField] float mouseSensitivity = 2f;
        [SerializeField] float gravity = -24f;
        [SerializeField] Transform cameraPivot;
        [SerializeField] Transform leftHandAnchor;
        [SerializeField] Transform rightHandAnchor;
        [SerializeField] Renderer[] hideForOwner;

        public readonly NetworkVariable<Vector3> SyncedPosition = new();
        public readonly NetworkVariable<float> SyncedYaw = new();
        public PlayerHands Hands { get; private set; }
        public Transform CameraPivot => cameraPivot;
        CharacterController controller;
        Camera viewCamera;
        float pitch;
        float verticalVelocity;
        float sendTimer;
        Vector3 spawnPosition;

        public override void OnNetworkSpawn()
        {
            Players[OwnerClientId] = this;
            spawnPosition = transform.position;
            Hands = GetComponent<PlayerHands>();
            controller = GetComponent<CharacterController>();
            viewCamera = cameraPivot.GetComponent<Camera>();
            bool local = IsOwner;
            viewCamera.enabled = local;
            AudioListener listener = viewCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = local;
            foreach (Renderer r in hideForOwner) if (r != null) r.enabled = !local;
            if (local)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public override void OnNetworkDespawn() => Players.Remove(OwnerClientId);

        void Update()
        {
            if (!IsSpawned) return;
            if (!IsOwner)
            {
                transform.position = Vector3.Lerp(transform.position, SyncedPosition.Value, Time.deltaTime * 15f);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, SyncedYaw.Value, 0), Time.deltaTime * 15f);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
            if (Cursor.lockState != CursorLockMode.Locked || (NetworkGameManager.Instance != null && NetworkGameManager.Instance.TeamWon.Value)) return;

            float yaw = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            float lookY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            transform.Rotate(0, yaw, 0);
            pitch = Mathf.Clamp(pitch - lookY, -82f, 82f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);

            bool crouching = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            float speed = crouching ? crouchSpeed : (Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed);
            Vector3 input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")), 1f);
            Vector3 motion = transform.TransformDirection(input) * speed;
            if (controller.isGrounded && verticalVelocity < 0) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            motion.y = verticalVelocity;
            controller.height = Mathf.MoveTowards(controller.height, crouching ? 1.15f : 1.8f, Time.deltaTime * 5f);
            cameraPivot.localPosition = Vector3.up * (controller.height - 0.18f);
            controller.Move(motion * Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.Q)) Hands.RequestDrop(HandSlot.Left);
            if (Input.GetKeyDown(KeyCode.R)) Hands.RequestDrop(HandSlot.Right);
            if (Input.GetMouseButton(0)) Hands.RequestUse(HandSlot.Left);
            if (Input.GetMouseButton(1)) Hands.RequestUse(HandSlot.Right);

            sendTimer -= Time.deltaTime;
            if (sendTimer <= 0f)
            {
                sendTimer = 0.05f;
                SubmitTransformServerRpc(transform.position, transform.eulerAngles.y);
            }
        }

        [ServerRpc]
        void SubmitTransformServerRpc(Vector3 position, float yaw)
        {
            if ((position - SyncedPosition.Value).sqrMagnitude > 25f) position = SyncedPosition.Value + Vector3.ClampMagnitude(position - SyncedPosition.Value, 5f);
            SyncedPosition.Value = position;
            SyncedYaw.Value = yaw;
        }

        public Transform GetHandAnchor(HandSlot slot) => slot == HandSlot.Left ? leftHandAnchor : rightHandAnchor;

        public void ServerResetPosition()
        {
            if (!IsServer) return;
            Vector3 p = spawnPosition + new Vector3((OwnerClientId % 2) * 1.3f, 0, (OwnerClientId / 2) * 1.3f);
            SyncedPosition.Value = p;
            TeleportOwnerClientRpc(p);
        }

        [ClientRpc]
        void TeleportOwnerClientRpc(Vector3 position)
        {
            if (!IsOwner) return;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
        }
    }
}
