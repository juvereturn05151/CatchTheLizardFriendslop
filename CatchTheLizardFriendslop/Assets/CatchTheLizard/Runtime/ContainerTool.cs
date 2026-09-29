using UnityEngine;
using Unity.Netcode;

namespace CatchTheLizard
{
    public sealed class ContainerTool : HoldableItem, IUsableItem
    {
        [SerializeField] float captureRange = 1.6f;
        [SerializeField] Renderer indicator;
        public readonly NetworkVariable<bool> ContainsLizard = new(false);

        public void ServerUse(NetworkPlayer user, HandSlot hand)
        {
            if (!IsServer || ContainsLizard.Value) return;
            LizardController lizard = FindFirstObjectByType<LizardController>();
            if (lizard != null && lizard.State.Value == LizardState.Stunned && Vector3.Distance(transform.position, lizard.transform.position) <= captureRange)
            {
                ContainsLizard.Value = true;
                lizard.ServerCapture(NetworkObjectId);
            }
        }

        protected override void Update()
        {
            base.Update();
            if (indicator != null) indicator.material.color = ContainsLizard.Value ? new Color(0.25f, 1f, 0.35f) : new Color(0.25f, 0.75f, 1f);
        }

        public override void ServerReset()
        {
            base.ServerReset();
            if (IsServer) ContainsLizard.Value = false;
        }
    }
}
