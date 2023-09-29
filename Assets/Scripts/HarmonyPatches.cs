using Assets.Scripts.Flight;
using HarmonyLib;
using ModApi.Flight;
using ModApi.Common;
using System.Collections.Generic;

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
    // Some override indices for time warp mode lists in this TimeManager class
    // Ideally these would be configurable, but for now the list of time warp modes
    // available are hard-coded.
    private static int PauseIndex = 0;
    private static int FirstSlowMoIndex = PauseIndex + 1;
    private static int LastSlowMoIndex = FirstSlowMoIndex + 5;
    private static int NormalSpeedIndex = LastSlowMoIndex + 1;
    private static int FirstFastForwardIndex = NormalSpeedIndex + 1;
    private static int LastFastForwardIndex = FirstFastForwardIndex + 2;
    private static int FirstWarpIndex = LastFastForwardIndex + 1;
    
    private static int SlowMoButtonIndex = NormalSpeedIndex - 2;
    private static int FastForwardButtonIndex = FirstFastForwardIndex;

    [HarmonyPatch(nameof(TimeManager.SetSlowMotionMode))]
    [HarmonyPrefix]
    public static bool SetSlowMotionMode_Patch(
        TimeManager __instance
        )
    {
        // Only update the time warp mode setting if not already in slow motion mode.
        var modeIndex = AccessTools.Field(typeof(TimeManager), "_modeIndex");
        int modeIndexVal = (int)modeIndex.GetValue(__instance);
        if ((modeIndexVal < FirstSlowMoIndex) || (modeIndexVal > LastSlowMoIndex))
        {
            __instance.SetMode(SlowMoButtonIndex);
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
        __instance.SetMode(NormalSpeedIndex);
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
        if ((modeIndexVal < FirstFastForwardIndex) || (modeIndexVal > LastFastForwardIndex))
        {
            __instance.SetMode(FastForwardButtonIndex);
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

        if ((modeIndex == PauseIndex) && (curModeIndex > PauseIndex))
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
            __instance.SetMode(PauseIndex);
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
    // the size of the _modes list to see if it matches the game's default size of 16, to know that
    // the override has not yet been applied.  There may be a better method to use that is only called
    // once, but I had issues performing all of these actions on other functions.  Particularly, trying
    // to patch a postfix on the constructor was a big headache that I could not get to work.
    private static int OriginalTimeWarpModeCount = 16;
    
    [HarmonyPatch(nameof(TimeManager.Update))]
    [HarmonyPostfix]
    public static void Update_Patch(TimeManager __instance)
    {
        // Obtain the private time warp mode list field so it can be checked and modified.
        var modes = AccessTools.Field(typeof(TimeManager), "_modes");
        List<ITimeMultiplierMode> modeList = (List<ITimeMultiplierMode>)modes.GetValue(__instance);

        if (Game.InFlightScene && (modeList.Count == OriginalTimeWarpModeCount))
        {
            // Obtain private field and property info objects, so they can be overridden.
            var unPauseIndex = AccessTools.Field(typeof(TimeManager), "_unPauseIndex");
            var slowMotion = AccessTools.Field(typeof(TimeManager), "_slowMotion");
            //var realTime = AccessTools.Field(typeof(TimeManager), "_realTime");
            var fastForward = AccessTools.Field(typeof(TimeManager), "_fastForward");
            var firstWarpModeInfo = AccessTools.Property(typeof(TimeManager), "FirstWarpMode");
            var firstWarpModeSetter = firstWarpModeInfo.GetSetMethod(true);
            TimeManager.TimeMultiplierMode slowMotionVar = (TimeManager.TimeMultiplierMode)slowMotion.GetValue(__instance);
            TimeManager.TimeMultiplierMode fastForwardVar = (TimeManager.TimeMultiplierMode)fastForward.GetValue(__instance);

            // Clear the existing time warp mode list so it can be overridden.
            modeList.Clear();

            // Ideally these would be configurable, but for now the list of time warp modes
            // available are hard-coded.
            
            modeList.Add(new TimeManager.TimeMultiplierMode(0.0, warp: false, "Paused")); // PauseIndex

            // Slow-Mo Modes
            modeList.Add(new TimeManager.TimeMultiplierMode(1f / 64f, warp: false));
            modeList.Add(new TimeManager.TimeMultiplierMode(1f / 32f, warp: false));
            modeList.Add(new TimeManager.TimeMultiplierMode(1f / 16f, warp: false));
            modeList.Add(new TimeManager.TimeMultiplierMode(1f / 8f, warp: false));
            modeList.Add(new TimeManager.TimeMultiplierMode(1f / 4f, warp: false, "Slow-Mo")); // SlowMoButtonIndex
            modeList.Add(new TimeManager.TimeMultiplierMode(1f / 2f, warp: false));
            
            modeList.Add(new TimeManager.TimeMultiplierMode(1f, warp: false)); // NormalSpeedIndex
            
            // Fast-Forward Physics Modes
            modeList.Add(new TimeManager.TimeMultiplierMode(2f, warp: false)); // FastForwardButtonIndex
            modeList.Add(new TimeManager.TimeMultiplierMode(4f, warp: false));
            modeList.Add(new TimeManager.TimeMultiplierMode(8f, warp: false));

            // Warp (Non-Physics) Modes
            modeList.Add(new TimeManager.TimeMultiplierMode(10.0)); // FirstWarpIndex
            modeList.Add(new TimeManager.TimeMultiplierMode(25.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(100.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(500.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(2500.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(10000.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(50000.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(250000.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(1000000.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(5000000.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(25000000.0));
            modeList.Add(new TimeManager.TimeMultiplierMode(100000000.0));

            unPauseIndex.SetValue(__instance, NormalSpeedIndex);
            slowMotionVar.TimeMultiplier = modeList[SlowMoButtonIndex].TimeMultiplier;
            fastForwardVar.TimeMultiplier = modeList[FastForwardButtonIndex].TimeMultiplier;
            firstWarpModeSetter.Invoke(__instance, new object[] { FirstWarpIndex });
        }
    }
}