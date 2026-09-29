using UnityEngine;

namespace CatchTheLizard
{
    public sealed class SprayTool : HoldableItem, IUsableItem
    {
        [SerializeField] float range = 4f;
        [SerializeField, Range(0.2f, 1f)] float coneDot = 0.72f;
        [SerializeField] float exposurePerUse = 0.12f;
        [SerializeField] ParticleSystem sprayParticles;
        float lastVisualUse;

        public void ServerUse(NetworkPlayer user, HandSlot hand)
        {
            if (!IsServer) return;
            PlaySprayClientRpc();
            LizardController lizard = FindFirstObjectByType<LizardController>();
            if (lizard == null || lizard.State.Value == LizardState.Captured) return;
            Vector3 origin = user.CameraPivot.position;
            Vector3 to = lizard.transform.position + Vector3.up * 0.15f - origin;
            if (to.magnitude > range || Vector3.Dot(user.CameraPivot.forward, to.normalized) < coneDot) return;
            bool blocked = Physics.Linecast(origin, lizard.transform.position + Vector3.up * 0.15f, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore);
            if (!blocked || hit.collider.GetComponentInParent<LizardController>() != null)
                lizard.ServerApplySpray(exposurePerUse);
        }

        [Unity.Netcode.ClientRpc]
        void PlaySprayClientRpc()
        {
            if (Time.time - lastVisualUse < 0.08f) return;
            lastVisualUse = Time.time;
            if (sprayParticles != null) sprayParticles.Play();
        }
    }
}
