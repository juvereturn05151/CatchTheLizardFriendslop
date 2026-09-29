using UnityEngine;
using Unity.Netcode;

namespace CatchTheLizard
{
    public sealed class ContainerTool : HoldableItem
    {
        [SerializeField] float placementRange = 2.2f;
        [SerializeField] Vector3 capturedLocalPosition = new(0f, 0.38f, 0f);
        [SerializeField] Renderer indicator;
        public readonly NetworkVariable<bool> ContainsLizard = new(false);

        public bool ServerTryStoreLizard(NetworkPlayer user, LizardController lizard)
        {
            if (!IsServer || ContainsLizard.Value || lizard == null || !lizard.IsHeld) return false;
            if (lizard.HolderClientId.Value != user.OwnerClientId || Vector3.Distance(user.transform.position, transform.position) > placementRange) return false;
            ContainsLizard.Value = true;
            lizard.ServerCapture(NetworkObjectId, capturedLocalPosition);
            return true;
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
