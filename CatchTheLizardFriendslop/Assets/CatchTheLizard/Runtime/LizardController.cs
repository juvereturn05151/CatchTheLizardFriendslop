using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public sealed class LizardController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] float idleSpeed = 0.9f;
        [SerializeField] float fleeSpeed = 6.2f;
        [SerializeField] float alertRadius = 5.5f;
        [SerializeField] float safeDistance = 7.5f;
        [SerializeField] Vector2 roomHalfExtents = new(8.5f, 6.5f);
        [Header("Spray / Stun")]
        [SerializeField] float stunThreshold = 1f;
        [SerializeField] float stunDuration = 5f;
        [SerializeField] float sprayRecoveryRate = 0.16f;
        [SerializeField] Renderer[] visuals;

        public readonly NetworkVariable<LizardState> State = new(LizardState.Idle);
        public readonly NetworkVariable<float> SprayAmount = new();
        public readonly NetworkVariable<Vector3> SyncedPosition = new();
        public readonly NetworkVariable<float> SyncedYaw = new();
        public readonly NetworkVariable<ulong> CapturedByItem = new(PlayerHands.Empty);
        Vector3 startPosition;
        Vector3 velocity;
        Vector3 wanderDirection;
        float stateTimer;
        float decisionTimer;

        void Awake() => startPosition = transform.position;

        public override void OnNetworkSpawn()
        {
            if (IsServer) { SyncedPosition.Value = transform.position; SyncedYaw.Value = transform.eulerAngles.y; }
        }

        void Update()
        {
            bool visible = State.Value != LizardState.Captured;
            foreach (Renderer r in visuals) if (r != null) r.enabled = visible;
            GetComponent<Collider>().enabled = visible;
            if (!IsServer)
            {
                transform.position = Vector3.Lerp(transform.position, SyncedPosition.Value, Time.deltaTime * 18f);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, SyncedYaw.Value, 0), Time.deltaTime * 18f);
                return;
            }
            ServerTick();
            SyncedPosition.Value = transform.position;
            SyncedYaw.Value = transform.eulerAngles.y;
        }

        void ServerTick()
        {
            if (State.Value == LizardState.Captured) return;
            if (State.Value == LizardState.Stunned)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) { State.Value = LizardState.Fleeing; SprayAmount.Value = stunThreshold * 0.35f; }
                return;
            }
            SprayAmount.Value = Mathf.Max(0, SprayAmount.Value - sprayRecoveryRate * Time.deltaTime);

            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            NetworkPlayer nearest = players.OrderBy(p => (p.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
            float distance = nearest == null ? 999f : Vector3.Distance(nearest.transform.position, transform.position);
            if (distance < alertRadius) { State.Value = LizardState.Fleeing; stateTimer = 1.4f; }
            else if (State.Value == LizardState.Fleeing)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f && distance > safeDistance) State.Value = Random.value < 0.4f ? LizardState.Hiding : LizardState.Idle;
            }

            decisionTimer -= Time.deltaTime;
            Vector3 desired;
            if (State.Value == LizardState.Fleeing && nearest != null)
            {
                Vector3 away = transform.position - nearest.transform.position; away.y = 0;
                desired = (away.normalized + Random.insideUnitSphere * 0.28f).normalized;
            }
            else if (State.Value == LizardState.Hiding)
            {
                LizardHidePoint hide = FindObjectsByType<LizardHidePoint>(FindObjectsSortMode.None).OrderBy(p => (p.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
                desired = hide == null ? wanderDirection : (hide.transform.position - transform.position).normalized;
                if (hide != null && Vector3.Distance(transform.position, hide.transform.position) < 0.45f) { State.Value = LizardState.Idle; stateTimer = Random.Range(1f, 2.5f); }
            }
            else
            {
                if (decisionTimer <= 0f) { decisionTimer = Random.Range(0.8f, 2.2f); wanderDirection = Quaternion.Euler(0, Random.Range(-100f, 100f), 0) * (wanderDirection == Vector3.zero ? Vector3.forward : wanderDirection); }
                desired = wanderDirection;
            }
            desired.y = 0;
            Vector3 local = transform.position;
            if (Mathf.Abs(local.x) > roomHalfExtents.x - 0.7f) desired.x = -Mathf.Sign(local.x);
            if (Mathf.Abs(local.z) > roomHalfExtents.y - 0.7f) desired.z = -Mathf.Sign(local.z);
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, desired, 0.75f, ~0, QueryTriggerInteraction.Ignore)) desired = Quaternion.Euler(0, 110f, 0) * desired;
            float speed = State.Value == LizardState.Fleeing ? fleeSpeed : idleSpeed;
            velocity = Vector3.Lerp(velocity, desired.normalized * speed, Time.deltaTime * 8f);
            transform.position += velocity * Time.deltaTime;
            transform.position = new Vector3(Mathf.Clamp(transform.position.x, -roomHalfExtents.x, roomHalfExtents.x), 0.16f, Mathf.Clamp(transform.position.z, -roomHalfExtents.y, roomHalfExtents.y));
            if (velocity.sqrMagnitude > 0.05f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocity.normalized), Time.deltaTime * 14f);
        }

        public void ServerApplySpray(float amount)
        {
            if (!IsServer || State.Value is LizardState.Stunned or LizardState.Captured) return;
            SprayAmount.Value = Mathf.Min(stunThreshold, SprayAmount.Value + amount);
            if (SprayAmount.Value >= stunThreshold) { State.Value = LizardState.Stunned; stateTimer = stunDuration; velocity = Vector3.zero; }
        }

        public void ServerPush(Vector3 direction, float force)
        {
            if (!IsServer || State.Value is LizardState.Stunned or LizardState.Captured) return;
            velocity += new Vector3(direction.x, 0, direction.z).normalized * force;
            State.Value = LizardState.Fleeing;
            stateTimer = 1.8f;
        }

        public void ServerCapture(ulong containerId)
        {
            if (!IsServer || State.Value != LizardState.Stunned) return;
            CapturedByItem.Value = containerId;
            State.Value = LizardState.Captured;
        }

        public void ServerReset()
        {
            if (!IsServer) return;
            transform.position = startPosition;
            velocity = Vector3.zero;
            SprayAmount.Value = 0;
            CapturedByItem.Value = PlayerHands.Empty;
            State.Value = LizardState.Idle;
        }

        void OnGUI()
        {
            if (State.Value == LizardState.Captured) return;
            Vector3 screen = Camera.main == null ? Vector3.zero : Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 0.65f);
            if (screen.z <= 0) return;
            Rect rect = new(screen.x - 55, Screen.height - screen.y, 110, 20);
            GUI.Box(rect, $"{State.Value} {SprayAmount.Value * 100:0}%");
        }
    }
}
