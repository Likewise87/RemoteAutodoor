using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.RemoteAutodoor
{
    public class CompProperties_RemoteAutodoor : CompProperties
    {
        public bool onlyWhenPowered = true;
        public bool verboseLogs = false; // set true to debug which method is used
        public CompProperties_RemoteAutodoor() { compClass = typeof(CompRemoteAutodoor); }
    }

    public class CompRemoteAutodoor : ThingComp
    {
        private static readonly Type TDoor = typeof(Building_Door);

        // Method handles for multiple RimWorld versions
        private static readonly MethodInfo MI_DoorOpen = AccessTools.Method(TDoor, "DoorOpen");
        private static readonly MethodInfo MI_Open = AccessTools.Method(TDoor, "Open");
        private static readonly MethodInfo MI_TryClose = AccessTools.Method(TDoor, "TryClose", new[] { typeof(bool) });
        private static readonly MethodInfo MI_DoorTryCloseNoArg = AccessTools.Method(TDoor, "DoorTryClose", Type.EmptyTypes);
        private static readonly MethodInfo MI_DoorTryCloseBool = AccessTools.Method(TDoor, "DoorTryClose", new[] { typeof(bool) });
        private static readonly MethodInfo MI_StartManualOpenBy = AccessTools.Method(TDoor, "StartManualOpenBy", new[] { typeof(Pawn) });
        private static readonly MethodInfo MI_StartManualCloseBy = AccessTools.Method(TDoor, "StartManualCloseBy", new[] { typeof(Pawn) });
        private static readonly MethodInfo MI_SetHoldOpen = AccessTools.Method(TDoor, "SetHoldOpen", new[] { typeof(bool) });
        private static readonly MethodInfo MI_ToggleHoldOpen = AccessTools.Method(TDoor, "ToggleHoldOpen");

        // Field handles for low-level fallback
        private static readonly FieldInfo FI_holdOpenInt = AccessTools.Field(TDoor, "holdOpenInt");
        private static readonly FieldInfo FI_openInt = AccessTools.Field(TDoor, "openInt");
        private static readonly FieldInfo FI_ticksUntilClose = AccessTools.Field(TDoor, "ticksUntilClose");

        private Building_Door Door => parent as Building_Door;
        private CompPowerTrader Power => parent.GetComp<CompPowerTrader>();
        private bool IsPlayerOwned => parent.Faction == Faction.OfPlayer;
        private CompProperties_RemoteAutodoor Props => (CompProperties_RemoteAutodoor)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Door == null) yield break;
            if (!IsPlayerOwned) yield break;

            // keep the power guard: it already allows non-powered doors to work
            if (Props.onlyWhenPowered && Power != null && !Power.PowerOn) yield break;

            bool held = Door.HoldOpen;
            yield return new Command_Action
            {
                defaultLabel = held ? "Remote: Close now" : "Remote: Open & hold",
                defaultDesc = "Toggle this door remotely.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/RemoteAutodoor"),
                action = () =>
                {
                    try
                    {
                        if (!Door.HoldOpen)
                        {
                            SetHoldOpenSafe(Door, true);
                            if (!OpenNow(Door)) FallbackFlipOpenVisuals(Door, true, 180);
                            MoteMaker.ThrowText(Door.DrawPos, Door.Map, "Open", 1.2f);
                        }
                        else
                        {
                            SetHoldOpenSafe(Door, false);
                            if (!CloseNow(Door)) FallbackFlipOpenVisuals(Door, false);
                            MoteMaker.ThrowText(Door.DrawPos, Door.Map, "Close", 1.2f);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[RemoteAutodoor] Toggle failed: {e}");
                    }
                }
            };
        }

        // ---- Open & Close logic ----

        private bool OpenNow(Building_Door d)
        {
            if (Call(MI_DoorOpen, d)) return LogHit("DoorOpen");
            if (Call(MI_Open, d)) return LogHit("Open");
            if (Call(MI_StartManualOpenBy, d, null)) return LogHit("StartManualOpenBy(null)");
            return false;
        }

        private bool CloseNow(Building_Door d)
        {
            if (Call(MI_DoorTryCloseBool, d, true)) return LogHit("DoorTryClose(true)");
            if (Call(MI_TryClose, d, true)) return LogHit("TryClose(true)");
            if (Call(MI_DoorTryCloseNoArg, d)) return LogHit("DoorTryClose()");
            if (Call(MI_StartManualCloseBy, d, null)) return LogHit("StartManualCloseBy(null)");
            return false;
        }

        // ---- Hold open setter ----

        private void SetHoldOpenSafe(Building_Door d, bool value)
        {
            if (Call(MI_SetHoldOpen, d, value)) { LogHit($"SetHoldOpen({value})"); return; }
            if (MI_ToggleHoldOpen != null && d.HoldOpen != value)
            {
                MI_ToggleHoldOpen.Invoke(d, null);
                LogHit("ToggleHoldOpen()");
                return;
            }
            if (FI_holdOpenInt != null)
            {
                FI_holdOpenInt.SetValue(d, value);
                LogHit($"holdOpenInt={value} (field)");
                return;
            }
            LogMiss("HoldOpen setter");
        }

        // ---- Fallback when no internal methods worked ----

        private void FallbackFlipOpenVisuals(Building_Door d, bool open, int keepOpenTicks = 0)
        {
            try
            {
                if (FI_openInt != null) { FI_openInt.SetValue(d, open); LogHit($"openInt={open} (field)"); }
                if (keepOpenTicks > 0 && FI_ticksUntilClose != null)
                {
                    FI_ticksUntilClose.SetValue(d, keepOpenTicks);
                    LogHit($"ticksUntilClose={keepOpenTicks} (field)");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[RemoteAutodoor] FallbackFlipOpenVisuals failed: {e}");
            }
        }

        // ---- Reflection helpers ----

        private static bool Call(MethodInfo mi, object instance, params object[] args)
        {
            if (mi == null) return false;
            try { mi.Invoke(instance, args); return true; } catch { return false; }
        }

        private bool LogHit(string what)
        {
            if (Props.verboseLogs) Log.Message($"[RemoteAutodoor] Used: {what}");
            return true;
        }

        private void LogMiss(string what)
        {
            if (Props.verboseLogs) Log.Message($"[RemoteAutodoor] Fallback path used for {what}");
        }
    }
}
