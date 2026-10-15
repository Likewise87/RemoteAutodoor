using HarmonyLib;
using Verse;

namespace TSA.RemoteAutodoor
{
    public class RemoteAutodoorMod : Mod
    {
        public RemoteAutodoorMod(ModContentPack content) : base(content)
        {
            new Harmony("TSA.RemoteAutodoor").PatchAll();
        }
    }
}
