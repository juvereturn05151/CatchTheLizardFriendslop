using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public sealed class LizardController : HoldableItem
    {
        [Header("Surface Movement")]
        [SerializeField] float floorMovementSpeed = 0.9f;
        [SerializeField] float wallMovementSpeed = 1.6f;
        [SerializeField] float fleeSpeed = 6.2f;
        [SerializeField] float playerDetectionRadius = 5.5f;
        [SerializeField] float safeDistance = 7.5f;
        [SerializeField] float surfaceDetectionDistance = 0.65f;
        [SerializeField] float surfaceOffset = 0.16f;
        [SerializeField] float surfaceTransitionSpeed = 9f;
        [SerializeField] float movementProbeRadius = 0.1f;
        [SerializeField] float maximumMovementStep = 0.18f;
        [SerializeField, Range(0.1f, 0.5f)] float wallNormalUpDot = 0.35f;
        [SerializeField, Range(0f, 1f)] float climbPreference = 0.85f;
        [SerializeField, Range(0f, 2f)] float wallFleeWeighting = 1.2f;

        [Header("Spray / Catch")]
        [SerializeField] float stunThreshold = 1f;
        [SerializeField] float stunDuration = 5f;
        [SerializeField] float sprayRecoveryRate = 0.16f;
        [SerializeField] bool stunnedWallLizardFalls = true;
        [SerializeField] float fallGravity = 18f;
        [SerializeField] float catchInteractionDistance = 2.7f;
        [SerializeField] float droppedLizardRecoveryTime = 2.5f;
        [SerializeField] Renderer[] visuals;

        public readonly NetworkVariable<LizardState> State = new(LizardState.Idle);
        public readonly NetworkVariable<float> SprayAmount = new();
        public readonly NetworkVariable<Vector3> SyncedPosition = new();
        public readonly NetworkVariable<Quaternion> SyncedRotation = new();
        public readonly NetworkVariable<Vector3> SurfaceNormal = new(Vector3.up);
        public readonly NetworkVariable<ulong> CapturedByItem = new(PlayerHands.Empty);
        public override float PickupDistance => catchInteractionDistance;

        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 velocity;
        Vector3 wanderDirection = Vector3.forward;
        Vector3 currentNormal = Vector3.up;
        Vector3 targetNormal = Vector3.up;
        Vector3 fallVelocity;
        Vector3 capturedLocalPosition;
        float stateTimer;
        float decisionTimer;
        readonly RaycastHit[] castHits = new RaycastHit[16];
        readonly Collider[] overlapHits = new Collider[24];

        protected override void Awake()
        {
            base.Awake();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            // The placed capsule may touch the floor. Establish a valid starting pose
            // before casting; an overlapping sphere cast has no usable surface normal.
            Physics.SyncTransforms();
            ResolvePenetrations(null);
            StickToSurface();
            PublishTransform();
        }

        public override bool CanPickup(NetworkPlayer player)
        {
            return !IsHeld && State.Value == LizardState.Stunned &&
                   Vector3.Distance(player.transform.position, transform.position) <= catchInteractionDistance;
        }

        protected override void Update()
        {
            // Network variables still contain their defaults while the connection menu
            // is open. Interpolating them here would pull the scene lizard into the floor.
            if (!IsSpawned) return;

            bool captured = State.Value == LizardState.Captured;
            bool held = State.Value == LizardState.Held && IsHeld;
            foreach (Renderer r in visuals) if (r != null) r.enabled = true;
            if (ItemCollider != null) ItemCollider.enabled = !captured && !held;

            if (held)
            {
                FollowHeldAnchor();
                PublishOrInterpolate();
                return;
            }
            if (captured)
            {
                if (IsServer) { FollowContainer(); PublishTransform(); }
                else
                {
                    transform.position = Vector3.Lerp(transform.position, SyncedPosition.Value, Time.deltaTime * 24f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, SyncedRotation.Value, Time.deltaTime * 24f);
                }
                return;
            }
            if (!IsServer)
            {
                transform.position = Vector3.Lerp(transform.position, SyncedPosition.Value, Time.deltaTime * 18f);
                transform.rotation = Quaternion.Slerp(transform.rotation, SyncedRotation.Value, Time.deltaTime * 18f);
                return;
            }

            if (State.Value == LizardState.Falling) ServerFallTick();
            else ServerSurfaceTick();
            PublishTransform();
        }

        void FollowHeldAnchor()
        {
            NetworkPlayer owner = NetworkPlayer.FindByClientId(HolderClientId.Value);
            if (owner == null) return;
            Transform anchor = owner.GetHandAnchor((HandSlot)HeldHand.Value);
            transform.SetPositionAndRotation(anchor.TransformPoint(heldLocalPosition), anchor.rotation * Quaternion.Euler(heldLocalEuler));
        }

        void FollowContainer()
        {
            if (NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(CapturedByItem.Value, out NetworkObject container)) return;
            transform.SetPositionAndRotation(container.transform.TransformPoint(capturedLocalPosition), container.transform.rotation);
        }

        void PublishOrInterpolate()
        {
            if (IsServer) PublishTransform();
            else
            {
                transform.position = Vector3.Lerp(transform.position, SyncedPosition.Value, Time.deltaTime * 24f);
                transform.rotation = Quaternion.Slerp(transform.rotation, SyncedRotation.Value, Time.deltaTime * 24f);
            }
        }

        void PublishTransform()
        {
            SyncedPosition.Value = transform.position;
            SyncedRotation.Value = transform.rotation;
            SurfaceNormal.Value = currentNormal;
        }

        void ServerSurfaceTick()
        {
            if (State.Value == LizardState.Stunned)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    State.Value = LizardState.Fleeing;
                    SprayAmount.Value = stunThreshold * 0.35f;
                    stateTimer = 1.4f;
                }
                StickToSurface();
                return;
            }

            SprayAmount.Value = Mathf.Max(0, SprayAmount.Value - sprayRecoveryRate * Time.deltaTime);
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            NetworkPlayer nearest = players.OrderBy(p => (p.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
            float distance = nearest == null ? 999f : Vector3.Distance(nearest.transform.position, transform.position);
            if (distance < playerDetectionRadius) { State.Value = LizardState.Fleeing; stateTimer = 1.4f; }
            else if (State.Value == LizardState.Fleeing)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f && distance > safeDistance) State.Value = Random.value < 0.4f ? LizardState.Hiding : LizardState.Idle;
            }

            Vector3 desired = ChooseSurfaceDirection(nearest);
            desired = Vector3.ProjectOnPlane(desired, currentNormal).normalized;
            if (desired.sqrMagnitude < 0.01f) desired = Vector3.ProjectOnPlane(transform.forward, currentNormal).normalized;

            float speed = State.Value == LizardState.Fleeing ? fleeSpeed : (Vector3.Dot(currentNormal, Vector3.up) > 0.65f ? floorMovementSpeed : wallMovementSpeed);
            velocity = Vector3.Lerp(velocity, desired * speed, Time.deltaTime * 8f);
            MoveSafelyAlongSurface(velocity.normalized, velocity.magnitude * Time.deltaTime, nearest);
            StickToSurface();
            AlignToSurface(velocity.sqrMagnitude > 0.02f ? velocity.normalized : transform.forward);
            ResolvePenetrations(nearest);
        }

        void MoveSafelyAlongSurface(Vector3 direction, float distance, NetworkPlayer nearest)
        {
            if (direction.sqrMagnitude < 0.01f || distance <= 0f) return;
            direction = Vector3.ProjectOnPlane(direction, currentNormal).normalized;
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / Mathf.Max(0.04f, maximumMovementStep)), 1, 12);
            float stepDistance = distance / steps;
            for (int i = 0; i < steps; i++)
            {
                if (TrySurfaceStep(direction, stepDistance, out RaycastHit blocker)) continue;

                if (TryTransition(blocker, direction))
                {
                    direction = Vector3.ProjectOnPlane(IsWall(currentNormal) ? Vector3.up + direction * 0.25f : direction, currentNormal).normalized;
                    velocity = direction * velocity.magnitude;
                    continue;
                }

                Vector3 fallback = ChooseBlockedDirection(blocker, nearest, stepDistance);
                if (fallback.sqrMagnitude > 0.01f && TrySurfaceStep(fallback, stepDistance, out _))
                {
                    direction = fallback;
                    velocity = fallback * velocity.magnitude;
                    continue;
                }

                velocity = Vector3.zero;
                break;
            }
        }

        bool TrySurfaceStep(Vector3 direction, float distance, out RaycastHit blocker)
        {
            blocker = default;
            if (direction.sqrMagnitude < 0.01f) return false;
            direction = Vector3.ProjectOnPlane(direction, currentNormal).normalized;
            Vector3 origin = GetColliderBounds().center + currentNormal * 0.015f;
            float leadingClearance = Mathf.Max(0f, GetMaximumColliderExtent() - movementProbeRadius);
            if (TrySphereCast(origin, movementProbeRadius, direction, distance + leadingClearance + 0.02f, out blocker)) return false;

            Vector3 candidate = transform.position + direction * distance;
            if (!TryGetSupport(candidate, currentNormal, out RaycastHit support)) return false;
            if (!IsSameSurfaceKind(currentNormal, support.normal)) return false;

            Vector3 snapped = GetTransformPositionForSurface(support.point, support.normal);
            if (Physics.CheckSphere(snapped, movementProbeRadius * 0.8f, ~0, QueryTriggerInteraction.Ignore))
            {
                // CheckSphere can include the current support or the lizard itself; a short
                // swept test from the current valid position is the authoritative blocker.
                if (TrySphereCast(GetColliderBounds().center, movementProbeRadius * 0.8f, direction, distance, out blocker)) return false;
            }
            currentNormal = targetNormal = support.normal.normalized;
            transform.position = snapped;
            return true;
        }

        bool TryTransition(RaycastHit hit, Vector3 incomingDirection)
        {
            if (hit.collider == null || hit.distance <= 0f || hit.collider.GetComponent<LizardClimbableSurface>() == null) return false;
            Vector3 nextNormal = hit.normal.normalized;
            bool floorToWall = IsFloor(currentNormal) && IsWall(nextNormal);
            bool wallToFloor = IsWall(currentNormal) && IsFloor(nextNormal);
            if (!floorToWall && !wallToFloor) return false;
            if (floorToWall && State.Value != LizardState.Fleeing && Random.value > climbPreference) return false;

            currentNormal = targetNormal = nextNormal;
            transform.position = GetTransformPositionForSurface(hit.point, nextNormal);
            Vector3 nextDirection = floorToWall
                ? Vector3.ProjectOnPlane(Vector3.up + incomingDirection * 0.2f, nextNormal).normalized
                : Vector3.ProjectOnPlane(incomingDirection, nextNormal).normalized;
            if (nextDirection.sqrMagnitude > 0.01f) velocity = nextDirection * velocity.magnitude;
            return true;
        }

        Vector3 ChooseBlockedDirection(RaycastHit blocker, NetworkPlayer nearest, float stepDistance)
        {
            Vector3 blockingNormal = blocker.collider == null ? Vector3.down : blocker.normal.normalized;
            return ChooseBlockedDirection(blockingNormal, nearest, stepDistance);
        }

        Vector3 ChooseBlockedDirection(Vector3 blockingNormal, NetworkPlayer nearest, float stepDistance)
        {
            Vector3 edge = Vector3.Cross(currentNormal, blockingNormal).normalized;
            if (edge.sqrMagnitude < 0.01f) edge = Vector3.Cross(currentNormal, transform.forward).normalized;
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, currentNormal).normalized;

            // At a wall/ceiling junction, horizontal movement is preferred. Pick the side
            // that increases distance from the nearest player, then try the other side and down.
            if (nearest != null && Vector3.Dot(edge, transform.position - nearest.transform.position) < 0f) edge = -edge;
            Vector3 reverse = Vector3.ProjectOnPlane(-velocity, currentNormal).normalized;
            Vector3[] candidates = IsWall(currentNormal)
                ? new[] { edge, -edge, down }
                : new[] { edge, -edge, reverse };
            foreach (Vector3 candidate in candidates)
            {
                if (candidate.sqrMagnitude < 0.01f) continue;
                Vector3 origin = GetColliderBounds().center + currentNormal * 0.015f;
                float leadingClearance = Mathf.Max(0f, GetMaximumColliderExtent() - movementProbeRadius);
                if (TrySphereCast(origin, movementProbeRadius, candidate, stepDistance + leadingClearance + 0.02f, out _)) continue;
                if (TryGetSupport(transform.position + candidate * stepDistance, currentNormal, out RaycastHit support) && IsSameSurfaceKind(currentNormal, support.normal))
                    return candidate;
            }
            return Vector3.zero;
        }

        void ResolvePenetrations(NetworkPlayer nearest)
        {
            if (ItemCollider == null) return;
            for (int pass = 0; pass < 4; pass++)
            {
                Bounds bounds = GetColliderBounds();
                int count = Physics.OverlapSphereNonAlloc(bounds.center, bounds.extents.magnitude + 0.02f, overlapHits, ~0, QueryTriggerInteraction.Ignore);
                bool corrected = false;
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlapHits[i];
                    if (other == null || other == ItemCollider || other.transform.IsChildOf(transform)) continue;
                    if (!Physics.ComputePenetration(ItemCollider, transform.position, transform.rotation,
                            other, other.transform.position, other.transform.rotation, out Vector3 separate, out float depth) || depth <= 0.0005f) continue;

                    transform.position += separate * (depth + 0.004f);
                    corrected = true;
                    // A capsule can legitimately overlap a little with either member of a
                    // floor/wall junction while its orientation is changing. Separate it,
                    // but do not treat that traversable contact as a reason to abandon the
                    // climb. An underside contact points down, so a ceiling is still always
                    // classified as blocking even if it was copied from a marked wall.
                    bool isTraversableContact = other.GetComponent<LizardClimbableSurface>() != null &&
                                                IsTraversableNormal(separate);
                    if (!isTraversableContact)
                    {
                        Vector3 fallback = ChooseBlockedDirection(separate, nearest, Mathf.Max(0.08f, maximumMovementStep));
                        if (fallback.sqrMagnitude < 0.01f && IsWall(currentNormal))
                            fallback = Vector3.ProjectOnPlane(Vector3.down, currentNormal).normalized;
                        velocity = fallback * Mathf.Max(wallMovementSpeed, velocity.magnitude);
                    }
                }
                if (!corrected) break;
            }
        }

        bool TryGetSupport(Vector3 position, Vector3 normal, out RaycastHit support)
        {
            Vector3 colliderOffset = GetColliderBounds().center - transform.position;
            Vector3 origin = position + colliderOffset + normal.normalized * 0.12f;
            if (!TrySphereCast(origin, movementProbeRadius, -normal.normalized, surfaceDetectionDistance + 0.25f, out support)) return false;
            return support.collider.GetComponent<LizardClimbableSurface>() != null && IsTraversableNormal(support.normal);
        }

        bool TrySphereCast(Vector3 origin, float radius, Vector3 direction, float distance, out RaycastHit closest)
        {
            closest = default;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction.normalized, castHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                if (hit.collider == null || hit.collider == ItemCollider || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                closest = hit;
            }
            return closest.collider != null;
        }

        bool IsTraversableNormal(Vector3 normal) => IsFloor(normal) || IsWall(normal);
        bool IsFloor(Vector3 normal) => Vector3.Dot(normal.normalized, Vector3.up) > 0.65f;
        bool IsWall(Vector3 normal) => Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) <= wallNormalUpDot;
        bool IsSameSurfaceKind(Vector3 a, Vector3 b) => (IsFloor(a) && IsFloor(b)) || (IsWall(a) && IsWall(b));

        float GetSurfaceClearance(Vector3 normal) => Mathf.Max(surfaceOffset, GetColliderExtent(normal) + 0.015f);

        Vector3 GetTransformPositionForSurface(Vector3 surfacePoint, Vector3 normal)
        {
            Vector3 colliderOffset = GetColliderBounds().center - transform.position;
            return surfacePoint + normal.normalized * GetSurfaceClearance(normal) - colliderOffset;
        }

        float GetColliderExtent(Vector3 direction)
        {
            if (ItemCollider == null) return movementProbeRadius;
            Vector3 extents = GetColliderBounds().extents;
            direction = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)).normalized;
            return Vector3.Dot(extents, direction);
        }

        float GetMaximumColliderExtent()
        {
            if (ItemCollider is CapsuleCollider capsule)
            {
                Vector3 scale = capsule.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float axisScale = capsule.direction == 0 ? scale.x : capsule.direction == 1 ? scale.y : scale.z;
                float radiusScale = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z)
                    : capsule.direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
                return Mathf.Max(capsule.height * axisScale * 0.5f, capsule.radius * radiusScale);
            }
            return GetColliderBounds().extents.magnitude;
        }

        Bounds GetColliderBounds()
        {
            // Collider.bounds can still describe the previous physics step when auto
            // sync is off. Derive the capsule from its current Transform so each movement
            // substep and rotation uses the new pose, not a cached physics pose.
            if (ItemCollider is CapsuleCollider capsule)
            {
                Vector3 scale = capsule.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Vector3 localAxis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                float axisScale = capsule.direction == 0 ? scale.x : capsule.direction == 1 ? scale.y : scale.z;
                float radiusScale = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z)
                    : capsule.direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
                float radius = capsule.radius * radiusScale;
                float halfSegment = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
                Vector3 axis = capsule.transform.TransformDirection(localAxis).normalized;
                Vector3 extents = Vector3.one * radius + new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)) * halfSegment;
                return new Bounds(capsule.transform.TransformPoint(capsule.center), extents * 2f);
            }
            return ItemCollider == null ? new Bounds(transform.position, Vector3.one * movementProbeRadius * 2f) : ItemCollider.bounds;
        }

        Vector3 ChooseSurfaceDirection(NetworkPlayer nearest)
        {
            if (State.Value == LizardState.Fleeing && nearest != null)
            {
                Vector3 away = Vector3.ProjectOnPlane(transform.position - nearest.transform.position, currentNormal).normalized;
                if (Vector3.Dot(currentNormal, Vector3.up) < 0.65f)
                    away = (away + Vector3.ProjectOnPlane(Vector3.up, currentNormal).normalized * wallFleeWeighting).normalized;
                return away;
            }
            if (State.Value == LizardState.Hiding)
            {
                LizardHidePoint hide = FindObjectsByType<LizardHidePoint>(FindObjectsSortMode.None)
                    .OrderBy(p => (p.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
                if (hide != null) return hide.transform.position - transform.position;
            }
            decisionTimer -= Time.deltaTime;
            if (decisionTimer <= 0f)
            {
                decisionTimer = Random.Range(0.8f, 2.2f);
                wanderDirection = Quaternion.AngleAxis(Random.Range(-100f, 100f), currentNormal) * wanderDirection;
            }
            return wanderDirection;
        }

        void StickToSurface()
        {
            if (TryGetSupport(transform.position, currentNormal, out RaycastHit contact) && IsSameSurfaceKind(currentNormal, contact.normal))
            {
                currentNormal = targetNormal = contact.normal.normalized;
                Vector3 desiredPosition = GetTransformPositionForSurface(contact.point, currentNormal);
                transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * surfaceTransitionSpeed);
                return;
            }

            if (IsFloor(currentNormal) && TrySphereCast(GetColliderBounds().center + Vector3.up * 0.15f, movementProbeRadius, Vector3.down, 1.2f, out RaycastHit floor) &&
                floor.collider.GetComponent<LizardClimbableSurface>() != null && IsFloor(floor.normal))
            {
                currentNormal = targetNormal = floor.normal.normalized;
                transform.position = Vector3.Lerp(transform.position, GetTransformPositionForSurface(floor.point, currentNormal), Time.deltaTime * surfaceTransitionSpeed);
            }
        }

        void AlignToSurface(Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, currentNormal).normalized;
            if (forward.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forward, currentNormal), Time.deltaTime * surfaceTransitionSpeed);
        }

        void ServerFallTick()
        {
            fallVelocity += Vector3.down * fallGravity * Time.deltaTime;
            Vector3 displacement = fallVelocity * Time.deltaTime;
            if (displacement.sqrMagnitude > 0f && Physics.SphereCast(transform.position, 0.12f, displacement.normalized, out RaycastHit hit, displacement.magnitude + surfaceOffset, ~0, QueryTriggerInteraction.Ignore) &&
                Vector3.Dot(hit.normal, Vector3.up) > 0.45f)
            {
                currentNormal = targetNormal = hit.normal.normalized;
                transform.position = GetTransformPositionForSurface(hit.point, currentNormal);
                velocity = fallVelocity = Vector3.zero;
                State.Value = LizardState.Stunned;
                stateTimer = Mathf.Max(stateTimer, droppedLizardRecoveryTime);
                AlignToSurface(Vector3.ProjectOnPlane(transform.forward, currentNormal));
                return;
            }
            transform.position += displacement;
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatForward, Vector3.up), Time.deltaTime * 3f);
        }

        public void ServerApplySpray(float amount)
        {
            if (!IsServer || State.Value is LizardState.Stunned or LizardState.Held or LizardState.Falling or LizardState.Captured) return;
            SprayAmount.Value = Mathf.Min(stunThreshold, SprayAmount.Value + amount);
            if (SprayAmount.Value < stunThreshold) return;
            velocity = Vector3.zero;
            stateTimer = stunDuration;
            if (stunnedWallLizardFalls && Vector3.Dot(currentNormal, Vector3.up) < 0.65f)
            {
                State.Value = LizardState.Falling;
                fallVelocity = Vector3.zero;
            }
            else State.Value = LizardState.Stunned;
        }

        public void ServerPush(Vector3 direction, float force)
        {
            if (!IsServer || State.Value is LizardState.Stunned or LizardState.Held or LizardState.Falling or LizardState.Captured) return;
            Vector3 surfacePush = Vector3.ProjectOnPlane(direction, currentNormal).normalized;
            if (surfacePush.sqrMagnitude < 0.01f) surfacePush = Vector3.ProjectOnPlane(transform.forward, currentNormal).normalized;
            velocity += surfacePush * force;
            State.Value = LizardState.Fleeing;
            stateTimer = 1.8f;
        }

        public override void ServerPickup(ulong clientId, HandSlot hand)
        {
            if (!IsServer || State.Value != LizardState.Stunned || IsHeld) return;
            base.ServerPickup(clientId, hand);
            State.Value = LizardState.Held;
            CapturedByItem.Value = PlayerHands.Empty;
            velocity = fallVelocity = Vector3.zero;
        }

        public override void ServerDrop(Vector3 position)
        {
            if (!IsServer || State.Value != LizardState.Held) return;
            base.ServerDrop(position);
            State.Value = LizardState.Falling;
            fallVelocity = Vector3.down * 0.5f;
            stateTimer = droppedLizardRecoveryTime;
        }

        public void ServerCapture(ulong containerId, Vector3 localPosition)
        {
            if (!IsServer || State.Value != LizardState.Held) return;
            HolderClientId.Value = PlayerHands.Empty;
            CapturedByItem.Value = containerId;
            capturedLocalPosition = localPosition;
            State.Value = LizardState.Captured;
            velocity = fallVelocity = Vector3.zero;
        }

        public override void ServerReset()
        {
            if (!IsServer) return;
            HolderClientId.Value = PlayerHands.Empty;
            transform.SetPositionAndRotation(startPosition, startRotation);
            currentNormal = targetNormal = Vector3.up;
            velocity = fallVelocity = Vector3.zero;
            SprayAmount.Value = 0f;
            CapturedByItem.Value = PlayerHands.Empty;
            State.Value = LizardState.Idle;
            PublishTransform();
        }

        void OnGUI()
        {
            if (State.Value is LizardState.Captured or LizardState.Held) return;
            Vector3 screen = Camera.main == null ? Vector3.zero : Camera.main.WorldToScreenPoint(transform.position + currentNormal * 0.35f);
            if (screen.z <= 0) return;
            Rect rect = new(screen.x - 65, Screen.height - screen.y, 130, 20);
            GUI.Box(rect, $"{State.Value} {SprayAmount.Value * 100:0}%");
        }
    }
}
