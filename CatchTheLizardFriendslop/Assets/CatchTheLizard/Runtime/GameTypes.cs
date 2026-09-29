namespace CatchTheLizard
{
    public enum HandSlot : byte { Left, Right }
    public enum LizardState : byte { Idle, Alert, Fleeing, Hiding, Stunned, Held, Falling, Captured }

    public interface IUsableItem
    {
        void ServerUse(NetworkPlayer user, HandSlot hand);
    }

    public interface IContinuousUsableItem
    {
        void ServerSetUsing(NetworkPlayer user, HandSlot hand, bool active);
    }
}
