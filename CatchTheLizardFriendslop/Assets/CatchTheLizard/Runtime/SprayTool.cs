using Unity.Netcode;
using UnityEngine;

namespace CatchTheLizard
{
    public sealed class SprayTool : HoldableItem, IContinuousUsableItem
    {
        [SerializeField] float range = 4f;
        [SerializeField, Range(0.2f, 1f)] float coneDot = 0.72f;
        [SerializeField] float exposurePerSecond = 1.35f;
        [SerializeField] ParticleSystem sprayParticles;
        [SerializeField] AudioSource sprayAudio;
        public readonly NetworkVariable<bool> IsSpraying = new(false);

        public void ServerSetUsing(NetworkPlayer user, HandSlot hand, bool active)
        {
            if (!IsServer) return;
            bool isCorrectHolder = IsHeld && HolderClientId.Value == user.OwnerClientId && HeldHand.Value == (byte)hand;
            IsSpraying.Value = active && isCorrectHolder;
        }

        protected override void Update()
        {
            base.Update();
            bool shouldPlay = IsHeld && IsSpraying.Value;
            if (sprayParticles != null)
            {
                if (shouldPlay && !sprayParticles.isPlaying) sprayParticles.Play();
                else if (!shouldPlay && sprayParticles.isPlaying) sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (sprayAudio != null)
            {
                if (shouldPlay && !sprayAudio.isPlaying) sprayAudio.Play();
                else if (!shouldPlay && sprayAudio.isPlaying) sprayAudio.Stop();
            }

            if (!IsServer || !shouldPlay) return;
            NetworkPlayer user = NetworkPlayer.FindByClientId(HolderClientId.Value);
            if (user == null) { IsSpraying.Value = false; return; }
            LizardController lizard = FindFirstObjectByType<LizardController>();
            if (lizard == null || lizard.State.Value == LizardState.Captured) return;
            Vector3 origin = user.CameraPivot.position;
            Vector3 target = lizard.transform.position;
            Vector3 to = target - origin;
            if (to.magnitude > range || Vector3.Dot(user.CameraPivot.forward, to.normalized) < coneDot) return;
            bool blocked = Physics.Linecast(origin, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore);
            if (!blocked || hit.collider.GetComponentInParent<LizardController>() != null)
                lizard.ServerApplySpray(exposurePerSecond * Time.deltaTime);
        }

        public override void ServerDrop(Vector3 position)
        {
            if (IsServer) IsSpraying.Value = false;
            base.ServerDrop(position);
        }

        public override void ServerReset()
        {
            if (IsServer) IsSpraying.Value = false;
            base.ServerReset();
        }
    }
}
