namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml.Serialization;
    using ModApi.Common;
    using ModApi.Craft.Propulsion;
    using ModApi.Settings.Core;
    using ModApi.Settings.Core.Events;
    using UnityEngine;


    /// <summary>
    /// The settings for the mod.
    /// </summary>
    /// <seealso cref="ModApi.Settings.Core.SettingsCategory{Assets.Scripts.ModSettings}" />
    public class ModSettings : SettingsCategory<ModSettings>
    {
        /// <summary>
        /// The mod settings instance.
        /// </summary>
        private static ModSettings _instance;

        // Some override indices for time warp mode lists in this TimeManager class
        // Ideally these would be configurable, but for now the list of time warp modes
        // available are hard-coded.
        public static int PauseIndex = 0;
        public static int FirstSlowMoIndex = PauseIndex + 1;
        public static int LastSlowMoIndex = FirstSlowMoIndex + 5;
        public static int NormalSpeedIndex = LastSlowMoIndex + 1;
        public static int FirstFastForwardIndex = NormalSpeedIndex + 1;
        public static int LastFastForwardIndex = FirstFastForwardIndex + 2;
        public static int FirstWarpIndex = LastFastForwardIndex + 1;
        
        public static float[] WarpModeArray = {
           /*  0 */ 0f,
           // Slow-Mo Modes
           /*  1 */ 1f / 64f,
           /*  2 */ 1f / 32f,
           /*  3 */ 1f / 16f,
           /*  4 */ 1f / 8f,
           /*  5 */ 1f / 4f,
           /*  6 */ 1f / 2f,
           // Normal Speed Mode
           /*  7 */ 1f,
           // Fast-Forward Physics Modes
           /*  8 */ 2f,
           /*  9 */ 4f,
           /* 10 */ 8f,
           // Warp (Non-Physics) Modes
           /* 11 */ 10f,
           /* 12 */ 25f,
           /* 13 */ 100f,
           /* 14 */ 500f,
           /* 15 */ 2500f,
           /* 16 */ 10000f,
           /* 17 */ 50000f,
           /* 18 */ 250000f,
           /* 19 */ 1000000f,
           /* 20 */ 5000000f,
           /* 21 */ 25000000f
        };

        public EnumSetting<KeyCode> NormalSpeedModeKeybind { get; private set; }
        public EnumSetting<KeyCode> SlowMotionModeKeybind { get; private set; }
        public EnumSetting<KeyCode> FastForwardModeKeybind { get; private set; }
        public EnumSetting<KeyCode> WarpModeKeybind { get; private set; }

        public BoolSetting PauseMaintainsWarpSpeed { get; private set; }

        public NumericSetting<int> DefaultSlowMotionSpeed { get; private set; }
        public NumericSetting<int> DefaultFastForwardSpeed { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModSettings"/> class.
        /// </summary>
        public ModSettings() : base("WarpSpeed")
        {
        }

        /// <summary>
        /// Gets the mod settings instance.
        /// </summary>
        /// <value>
        /// The mod settings instance.
        /// </value>
        public static ModSettings Instance => _instance ?? (_instance = Game.Instance.Settings.ModSettings.GetCategory<ModSettings>());
        private void OnWarpModeSpeed_Changed(object sender, SettingChangedEventArgs<int> e)
        {
            TimeManager_Patch._updateWarpModesNextFrame = true;
        }
        private void OnPauseMaintainsWarpSpeed_Changed(object sender, SettingChangedEventArgs<bool> e)
        {
            TimeManager_Patch._updateWarpModesNextFrame = true;
        }

        /// <summary>
        /// Initializes the settings in the category.
        /// </summary>
        protected override void InitializeSettings()
        {
            TimeWarpSettingDisplayDelegate timeWarpSettingDisplayDelegate = TimeWarpSettingDisplay;
            Func<int, string> displayFormatter = timeWarpSettingDisplayDelegate.Invoke;

            //this.TestSetting1 = this.CreateNumeric<float>("Test Setting 1", 1f, 10f, 1f)
            //    .SetDescription("A test setting that does nothing.")
            //    .SetDisplayFormatter(x => x.ToString("F1"))
            //    .SetDefault(2f);
            NormalSpeedModeKeybind = CreateEnum<KeyCode>("Normal Speed Mode Keybind", "normalSpeedModeKeybind")
                .SetDescription("Keybind to mirror the Normal Speed Mode (Play) button on the top right Time Panel.")
                .SetDefault(KeyCode.Slash);
            SlowMotionModeKeybind = CreateEnum<KeyCode>("Slow Motion Mode Keybind", "slowMotionModeKeybind")
                .SetDescription("Keybind to mirror the Slow Motion Mode button on the top right Time Panel.")
                .SetDefault(KeyCode.None);
            FastForwardModeKeybind = CreateEnum<KeyCode>("Fast Forward Mode Keybind", "fastForwardModeKeybind")
                .SetDescription("Keybind to mirror the Fast Forward mode button on the top right Time Panel.")
                .SetDefault(KeyCode.None);
            WarpModeKeybind = CreateEnum<KeyCode>("Warp Mode Keybind", "warpModeKeybind")
                .SetDescription("Keybind to mirror the Warp Mode button on the top right Time Panel.")
                .SetDefault(KeyCode.None);
            PauseMaintainsWarpSpeed = CreateBool("Pause Maintains Warp Speed", "pauseMaintainsWarpSpeed")
                .SetDescription("Unpausing will return time warp speed to the last speed before pausing.  If disabled, unpausing will return warp speed to 1.0x.")
                .SetDefault(true);
            DefaultSlowMotionSpeed = CreateNumeric<int>("Default Slow Motion Speed", 1, 6, 1, "defaultSlowMotionSpeed")
                .SetDescription("If not already in Slow Motion Mode, specifies the default time acceleration speed to set on entering Slow Motion Mode.")
                .SetDisplayFormatter(displayFormatter)
                .SetDefault(5);
            DefaultFastForwardSpeed = CreateNumeric<int>("Default Fast Foward Speed", 8, 10, 1, "defaultFastForwardSpeed")
                .SetDescription("If not already in Fast Foward Mode, specifies the default time acceleration speed to set on entering Fast Foward Mode.")
                .SetDisplayFormatter(displayFormatter)
                .SetDefault(8);

            DefaultSlowMotionSpeed.Changed += new EventHandler<SettingChangedEventArgs<int>>(OnWarpModeSpeed_Changed);
            DefaultFastForwardSpeed.Changed += new EventHandler<SettingChangedEventArgs<int>>(OnWarpModeSpeed_Changed);
            PauseMaintainsWarpSpeed.Changed += new EventHandler<SettingChangedEventArgs<bool>>(OnPauseMaintainsWarpSpeed_Changed);
        }
        
        public delegate string TimeWarpSettingDisplayDelegate(int setting);
        public static string TimeWarpSettingDisplay(int setting)
        {
            string text = "";
            float timeMultiplier = 0.0f;

            if (setting > 0 && setting < WarpModeArray.Length)
            {
                timeMultiplier = WarpModeArray[setting];
            }
            // Code borrowed from TimePanelController.SetWarpModeButtonText()
            if (timeMultiplier >= 1.0)
		    {
			    text = $"{(int)timeMultiplier:n0}<size=60%>x</size>";
		    }
		    else if (timeMultiplier > 0.0)
		    {
			    text = $"1/{(int)(1.0 / timeMultiplier):n0}<size=60%>x</size>";
		    }

            return text;
        }
    }
}