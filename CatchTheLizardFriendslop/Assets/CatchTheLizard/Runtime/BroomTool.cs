using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    public sealed class BroomTool : HoldableItem, IUsableItem
    {
        [SerializeField] float range = 2.8f;
        [SerializeField] float pushStrength = 6f;
        [SerializeField] float cooldown = 0.55f;
        readonly NetworkVariable<uint> swingSequence = new();
        float nextUse;
        Vector3 baseScale;

        protected override void Awake() { base.Awake(); baseScale = transform.localScale; }

        public void ServerUse(NetworkPlayer user, HandSlot hand)
        {
            if (!IsServer || Time.time < nextUse) return;
            nextUse = Time.time + cooldown;
            swingSequence.Value++;
            LizardController lizard = FindFirstObjectByType<LizardController>();
            if (lizard == null || lizard.State.Value is LizardState.Stunned or LizardState.Captured) return;
            Vector3 to = lizard.transform.position - user.transform.position;
            if (to.magnitude <= range && Vector3.Dot(user.transform.forward, to.normalized) > 0.25f)
                lizard.ServerPush((lizard.transform.position - user.transform.position).normalized, pushStrength);
        }

        protected override void Update()
        {
            base.Update();
            float pulse = Mathf.Sin(Time.time * 22f) * 0.08f;
            transform.localScale = baseScale * (1f + (Time.time < nextUse ? pulse : 0f));
        }
    }
}
