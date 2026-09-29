namespace CatchTheLizard
{
    public enum HandSlot : byte { Left, Right }
    public enum LizardState : byte { Idle, Alert, Fleeing, Hiding, Stunned, Captured }

    public interface IUsableItem
    {
        void ServerUse(NetworkPlayer user, HandSlot hand);
    }
}
