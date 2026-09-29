using UnityEngine;

namespace CatchTheLizard
{
    /// <summary>Marks geometry that the lizard may traverse and transition onto.</summary>
    [DisallowMultipleComponent]
    public sealed class LizardClimbableSurface : MonoBehaviour
    {
        [Tooltip("Allows this surface to be used as a floor as well as a wall.")]
        public bool allowHorizontal = true;
    }
}
