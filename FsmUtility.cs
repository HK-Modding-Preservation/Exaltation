using HutongGames.PlayMaker;

namespace Exaltation
{
    public static class FsmUtility
    {
        public static FsmStateAction GetAction(this PlayMakerFSM fsm, string stateName, int index)
        {
            foreach (FsmState t in fsm.FsmStates)
            {
                if (t.Name != stateName) continue;
                return t.Actions[index];
            }
            return null;
        }

        public static T GetAction<T>(this PlayMakerFSM fsm, string stateName, int index) where T : FsmStateAction
        {
            return GetAction(fsm, stateName, index) as T;
        }
    }
}
