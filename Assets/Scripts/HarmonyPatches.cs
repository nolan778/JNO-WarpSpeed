using Assets.Scripts.Flight;
using HarmonyLib;
using ModApi.Flight;
using ModApi.Common;
using System.Collections.Generic;
using UnityEngine;
using ModApi.Flight.UI;
using Assets.Scripts;

// Not Well-Known Tips for Harmony Patching:
// 1. Accessing private fields that don't have getter/setter functions:
//      a. Use Harmony's AccessTools.Field(typeof(ClassName), "fieldName") to
//         get the system reflection FieldInfo object for that field.
//      b. Use (CastType)fieldInfoObj.GetValue(objectInstance) to get the value of that field.
//      c. Use fieldInfoObj.SetValue(objectInstance, newValue) to set the value of that field.
//      Note: Three underscores are supposed to be able to be used in front of a private field
//            to make it accessible to Harmony, but I couldn't get that to work.
// 2. Calling a private setter function for a field:
//      a. Use Harmony's AccessTools.Property(typeof(ClassName), "PropertyName") to get the 
//         system reflection PropertyInfo object for that property.
//      b. Use propertyInfoObj.GetSetMethod(nonPublicBoolean) to obtaint the MethodInfo object
//         for the setter function, with the boolean parameter indicating whether the
//         setter is non-public.
//      c. Use methodInfoObj.Invoke(objectInstance, new object[] { newValue }) to call
//         the setter function, with the object array containing the parameters to pass
//         to the setter function.

// So far, this mod only patches the TimeManager class.
[HarmonyPatch(typeof(TimeManager))]
public static class TimeManager_Patch
{
    public static bool _updateWarpModesNextFrame = true;

    [HarmonyPatch(nameof(TimeManager.SetSlowMotionMode))]
    [HarmonyPrefix]
    public static bool SetSlowMotionMode_Patch(
        TimeManager __instance
        )
    {
        // Only update the time warp mode setting if not already in slow motion mode.
        var modeIndex = AccessTools.Field(typeof(TimeManager), "_modeIndex");
        int modeIndexVal = (int)modeIndex.GetValue(__instance);
        if ((modeIndexVal < ModSettings.FirstSlowMoIndex) || (modeIndexVal > ModSettings.LastSlowMoIndex))
        {
            __instance.SetMode(ModSettings.Instance.DefaultSlowMotionSpeed);
        }
        return false; // Never call the original
    }

    [HarmonyPatch(nameof(TimeManager.SetNormalSpeedMode))]
    [HarmonyPrefix]
    public static bool SetNormalSpeedMode_Patch(
        TimeManager __instance
        )
    {
        // Play button always sets to normal 1.0x speed mode.
        __instance.SetMode(ModSettings.NormalSpeedIndex);
        return false; // Never call the original
    }

    [HarmonyPatch(nameof(TimeManager.SetFastForwardMode))]
    [HarmonyPrefix]
    public static bool SetFastForwardMode_Patch(
        TimeManager __instance
        )
    {
        // Only update the time warp mode setting if not already in fast forward mode.
        var modeIndex = AccessTools.Field(typeof(TimeManager), "_modeIndex");
        int modeIndexVal = (int)modeIndex.GetValue(__instance);
        if ((modeIndexVal < ModSettings.FirstFastForwardIndex) || (modeIndexVal > ModSettings.LastFastForwardIndex))
        {
            __instance.SetMode(ModSettings.Instance.DefaultFastForwardSpeed);
        }
        return false; // Never call the original
    }


    // Specify which overloaded version of SetMode to patch
    [HarmonyPatch("SetMode", typeof(int), typeof(bool))]
    [HarmonyPrefix]
    public static bool SetMode_Patch(TimeManager __instance, int modeIndex, bool forceChange = false)
    {
        // Detect if the time warp mode index is commanded to pause and store off the
        // current time warp mode index for later use when unpausing.
        var modeIndexVar = AccessTools.Field(typeof(TimeManager), "_modeIndex");
        var unPauseIndex = AccessTools.Field(typeof(TimeManager), "_unPauseIndex");
        int curModeIndex = (int)modeIndexVar.GetValue(__instance);

        if ((modeIndex == ModSettings.PauseIndex) && (curModeIndex > ModSettings.PauseIndex))
        {
           unPauseIndex.SetValue(__instance, curModeIndex);
        }

        return true; // Allow original to be called
    }

    // Patching this function because the default unpause behavior is a bit odd.
    [HarmonyPatch(nameof(TimeManager.RequestPauseChange))]
    [HarmonyPrefix]
    public static bool RequestPauseChange_Patch(
        TimeManager __instance,
        bool paused, bool userInitiated
        )
    {
        if (paused)
        {
            __instance.SetMode(ModSettings.PauseIndex);
        }
        else
        {
            var unPauseIndex = AccessTools.Field(typeof(TimeManager), "_unPauseIndex");
            int unPauseIndexVal = (int)unPauseIndex.GetValue(__instance);
            
            __instance.SetMode(unPauseIndexVal);
        }
    
        return false; // Never call the original
    }

    // Using the Update method, while in a Flight Scene to override the default Time Warp Multiplier
    // List and associated indices on the first frame it is called.  This is handled by checking
    // the _updateWarpModesNextFrame variable, to know if the the override has yet been applied.
    // This variable is initialized to true on class creation, but it can also be reset to true by the
    // ModSettings class when the user changes one of the default Slow-Mo or Fast Forward time warp speeds.

    [HarmonyPatch(nameof(TimeManager.Update))]
    [HarmonyPostfix]
    public static void Update_Patch(TimeManager __instance)
    {
        // Obtain the private time warp mode list field so it can be checked and modified.
        var modes = AccessTools.Field(typeof(TimeManager), "_modes");
        List<ITimeMultiplierMode> modeList = (List<ITimeMultiplierMode>)modes.GetValue(__instance);

        if (ModApi.Common.Game.InFlightScene && _updateWarpModesNextFrame)
        {
            UpdateWarpModeList(__instance, ref modeList);
            
            //var modeIndexVar = AccessTools.Field(typeof(TimeManager), "_modeIndex");
            //int curModeIndex = (int)modeIndexVar.GetValue(__instance);
            //__instance.SetMode(curModeIndex);
        }

        if (ModApi.Common.Game.InFlightScene)
        {
            HandleCustomKeybinds(__instance);
        }

    }
    private static void UpdateWarpModeList(TimeManager __instance, ref List<ITimeMultiplierMode> modeList)
    {
        // Obtain private field and property info objects, so they can be overridden.
        var unPauseIndex = AccessTools.Field(typeof(TimeManager), "_unPauseIndex");
        var slowMotion = AccessTools.Field(typeof(TimeManager), "_slowMotion");
        var fastForward = AccessTools.Field(typeof(TimeManager), "_fastForward");
        var firstWarpModeInfo = AccessTools.Property(typeof(TimeManager), "FirstWarpMode");
        var firstWarpModeSetter = firstWarpModeInfo.GetSetMethod(true);
        TimeManager.TimeMultiplierMode slowMotionVar = (TimeManager.TimeMultiplierMode)slowMotion.GetValue(__instance);
        TimeManager.TimeMultiplierMode fastForwardVar = (TimeManager.TimeMultiplierMode)fastForward.GetValue(__instance);

        // Clear the existing time warp mode list so it can be overridden.
        modeList.Clear();

        for (int i = 0; i < ModSettings.WarpModeArray.Length; i++)
        {
            bool warp = i >= ModSettings.FirstWarpIndex;
            string name = null;
            if (i == 0)
            {
                name = "Pause";
            }
            else if (i == ModSettings.Instance.DefaultSlowMotionSpeed.Value)
            {
                name = "Slow-Mo";
            }

            modeList.Add(new TimeManager.TimeMultiplierMode(ModSettings.WarpModeArray[i], warp, name));
        }

        unPauseIndex.SetValue(__instance, ModSettings.NormalSpeedIndex);
        slowMotionVar.TimeMultiplier = modeList[ModSettings.Instance.DefaultSlowMotionSpeed.Value].TimeMultiplier;
        fastForwardVar.TimeMultiplier = modeList[ModSettings.Instance.DefaultFastForwardSpeed.Value].TimeMultiplier;
        firstWarpModeSetter.Invoke(__instance, new object[] { ModSettings.FirstWarpIndex });

        _updateWarpModesNextFrame = false;
    }

    private static void HandleCustomKeybinds(TimeManager __instance)
    {
        // Mirrors Slow Motion Mode Button Clicked on Time Panel at top right of Flight Scene UI
        if (Input.GetKeyDown(ModSettings.Instance.SlowMotionModeKeybind.Value))
        {
            __instance.SetSlowMotionMode();
        }
        // Mirrors Normal Speed Mode (Play) Button Clicked on Time Panel at top right of Flight Scene UI
        else if (Input.GetKeyDown(ModSettings.Instance.NormalSpeedModeKeybind.Value))
        {
            __instance.SetNormalSpeedMode();
        }
        // Mirrors Fast Forward Mode Button Clicked on Time Panel at top right of Flight Scene UI
        else if (Input.GetKeyDown(ModSettings.Instance.FastForwardModeKeybind.Value))
        {
            __instance.SetFastForwardMode();
        }
        // Mirrors Warp Mode Button Clicked on Time Panel at top right of Flight Scene UI
        // Code borrowed from TimePanelController.OnWarpModeClicked()
        else if (Input.GetKeyDown(ModSettings.Instance.WarpModeKeybind.Value))
        {
            if (!__instance.CurrentMode.WarpMode)
            {
                string failReason = null;
                if (__instance.CanSetTimeMultiplierMode(__instance.FirstWarpMode, out failReason))
                {
                    __instance.SetMode(__instance.FirstWarpMode);
                }
                else
                {
                    ModApi.Common.Game.Instance.FlightScene.FlightSceneUI.ShowMessage(failReason);
                }
            }
        }
    }
}