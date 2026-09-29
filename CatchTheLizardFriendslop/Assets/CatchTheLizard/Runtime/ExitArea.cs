using UnityEngine;

namespace CatchTheLizard
{
    public sealed class ExitArea : MonoBehaviour
    {
        public Vector3 Size = new(3f, 2.5f, 2f);
        public bool Contains(Vector3 point) => new Bounds(transform.position, Size).Contains(point);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.25f);
            Gizmos.DrawCube(transform.position, Size);
        }
    }
}
