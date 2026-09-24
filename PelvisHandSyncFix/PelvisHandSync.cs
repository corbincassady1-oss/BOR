using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;
using BoneLib;
using BoneLib.BoneMenu;

[assembly: MelonInfo(typeof(PelvisHandSync.Main), "PelvisHandSync", "1.3.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: MelonDependency("BoneLib")]

namespace PelvisHandSync
{
    public enum HandChoice { LEFT, RIGHT }
    public enum InputMode { MENU_ONLY, KEYBOARD, CONTROLLER, BOTH }
    public enum ControllerButton { TRIGGER, GRIP, PRIMARY, SECONDARY }
    public enum SwayType { ForwardBack, SideToSide }

    public class Main : MelonMod
    {
        private const string CategoryName = "PelvisHandSync";
        private static MelonPreferences_Category prefs;

        private static MelonPreferences_Entry<bool> prefEnabled;
        private static MelonPreferences_Entry<bool> prefSyncHand;
        private static MelonPreferences_Entry<InputMode> prefInputMode;
        private static MelonPreferences_Entry<KeyCode> prefToggleKey;
        private static MelonPreferences_Entry<HandChoice> prefToggleControllerHand;
        private static MelonPreferences_Entry<ControllerButton> prefToggleButton;
        private static MelonPreferences_Entry<bool> prefHipsEnabled;
        private static MelonPreferences_Entry<bool> prefChestEnabled;
        private static MelonPreferences_Entry<bool> prefWaistEnabled;
        private static MelonPreferences_Entry<bool> prefSwayEnabled;
        private static MelonPreferences_Entry<SwayType> prefSwayType;
        private static MelonPreferences_Entry<float> prefSwaySpeed;
        private static MelonPreferences_Entry<float> prefSwayAmount;
        private static MelonPreferences_Entry<bool> prefOnlyLowHp;
        private static MelonPreferences_Entry<float> prefMinHp;
        private static MelonPreferences_Entry<bool> prefHipRagdoll;
        private static MelonPreferences_Entry<float> prefRagdollHp;

        private static bool enabled;
        private static bool syncHand;
        private static InputMode inputMode;
        private static KeyCode toggleKey;
        private static HandChoice toggleControllerHand;
        private static ControllerButton toggleButton;
        private static bool hipsEnabled;
        private static bool chestEnabled;
        private static bool waistEnabled;
        private static bool swayEnabled;
        private static SwayType swayType;
        private static float swaySpeed;
        private static float swayAmount;
        private static bool onlyLowHp;
        private static float minHp;
        private static bool hipRagdoll;
        private static float ragdollHp;
        private static bool wasControllerButtonPressed;

        private Transform lastPelvis;
        private Quaternion pelvisBaseRotation = Quaternion.identity;
        private bool pelvisBaseCaptured;
        private bool ragdollState;

        public override void OnInitializeMelon()
        {
            try
            {
                prefs = MelonPreferences.CreateCategory(CategoryName);

                prefEnabled = prefs.CreateEntry("Enabled", true);
                prefSyncHand = prefs.CreateEntry("SyncHand", true);
                prefInputMode = prefs.CreateEntry("InputMode", InputMode.MENU_ONLY);
                prefToggleKey = prefs.CreateEntry("ToggleKey", KeyCode.F8);
                prefToggleControllerHand = prefs.CreateEntry("ToggleControllerHand", HandChoice.RIGHT);
                prefToggleButton = prefs.CreateEntry("ToggleButton", ControllerButton.PRIMARY);

                prefHipsEnabled = prefs.CreateEntry("HipsEnabled", true);
                prefChestEnabled = prefs.CreateEntry("ChestEnabled", true);
                prefWaistEnabled = prefs.CreateEntry("WaistEnabled", true);

                prefSwayEnabled = prefs.CreateEntry("SwayingAnimations", true);
                prefSwayType = prefs.CreateEntry("SwayType", SwayType.ForwardBack);
                prefSwaySpeed = prefs.CreateEntry("SwaySpeed", 3.0f);
                prefSwayAmount = prefs.CreateEntry("SwayAmount", 6.0f);
                prefOnlyLowHp = prefs.CreateEntry("OnlyAtLowHP", false);
                prefMinHp = prefs.CreateEntry("MinHP", 0.10f);
                prefHipRagdoll = prefs.CreateEntry("HipRagdollBelowHP", false);
                prefRagdollHp = prefs.CreateEntry("RagdollTriggerHP", 0.50f);

                enabled = prefEnabled.Value;
                syncHand = prefSyncHand.Value;
                inputMode = prefInputMode.Value;
                toggleKey = prefToggleKey.Value;
                toggleControllerHand = prefToggleControllerHand.Value;
                toggleButton = prefToggleButton.Value;
                hipsEnabled = prefHipsEnabled.Value;
                chestEnabled = prefChestEnabled.Value;
                waistEnabled = prefWaistEnabled.Value;
                swayEnabled = prefSwayEnabled.Value;
                swayType = prefSwayType.Value;
                swaySpeed = Mathf.Clamp(prefSwaySpeed.Value, 1f, 5f);
                swayAmount = Mathf.Clamp(prefSwayAmount.Value, 0f, 20f);
                onlyLowHp = prefOnlyLowHp.Value;
                minHp = Mathf.Clamp(prefMinHp.Value, 0.10f, 0.50f);
                hipRagdoll = prefHipRagdoll.Value;
                ragdollHp = Mathf.Clamp(prefRagdollHp.Value, 0.10f, 1f);

                BuildMenu();
                MelonLogger.Msg("PelvisHandSync 1.3.0 initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("PelvisHandSync initialization failed: " + ex);
            }
        }

        private void BuildMenu()
        {
            var page = Page.Root.CreatePage("Pelvis Hand Sync", Color.cyan, 0, true);

            page.CreateBool("Enabled", Color.green, enabled, v => { enabled = v; prefEnabled.Value = v; });
            page.CreateBool("Sync To Hand", Color.cyan, syncHand, v => { syncHand = v; prefSyncHand.Value = v; });
            page.CreateBool("Hips", Color.yellow, hipsEnabled, v => { hipsEnabled = v; prefHipsEnabled.Value = v; });
            page.CreateBool("Chest", Color.yellow, chestEnabled, v => { chestEnabled = v; prefChestEnabled.Value = v; });
            page.CreateBool("Waist", Color.yellow, waistEnabled, v => { waistEnabled = v; prefWaistEnabled.Value = v; });

            var sway = page.CreatePage("Swaying Animations", Color.cyan);
            sway.CreateBool("Enabled", Color.green, swayEnabled, v => { swayEnabled = v; prefSwayEnabled.Value = v; });
            sway.CreateEnum("Sway Type", Color.cyan, swayType, v => { swayType = v; prefSwayType.Value = v; });
            sway.CreateFloat("Speed", Color.cyan, swaySpeed, 1f, 5f, 0.1f, v => { swaySpeed = Mathf.Clamp(v, 1f, 5f); prefSwaySpeed.Value = swaySpeed; });
            sway.CreateFloat("Amount", Color.cyan, swayAmount, 0f, 20f, 0.5f, v => { swayAmount = Mathf.Clamp(v, 0f, 20f); prefSwayAmount.Value = swayAmount; });
            sway.CreateBool("Only At Low HP", Color.yellow, onlyLowHp, v => { onlyLowHp = v; prefOnlyLowHp.Value = v; });
            sway.CreateFloat("Minimum HP", Color.yellow, minHp * 100f, 10f, 50f, 1f, v => { minHp = Mathf.Clamp(v / 100f, 0.10f, 0.50f); prefMinHp.Value = minHp; });
            sway.CreateBool("Hip Ragdoll Below HP", Color.yellow, hipRagdoll, v => { hipRagdoll = v; prefHipRagdoll.Value = v; });
            sway.CreateFloat("Ragdoll Trigger HP", Color.yellow, ragdollHp * 100f, 10f, 100f, 1f, v => { ragdollHp = Mathf.Clamp(v / 100f, 0.10f, 1f); prefRagdollHp.Value = ragdollHp; });

            var input = page.CreatePage("Toggle Input", Color.white);
            input.CreateEnum("Input Mode", Color.white, inputMode, v => { inputMode = v; prefInputMode.Value = v; });
            input.CreateEnum("Controller Hand", Color.white, toggleControllerHand, v => { toggleControllerHand = v; prefToggleControllerHand.Value = v; });
            input.CreateEnum("Controller Button", Color.white, toggleButton, v => { toggleButton = v; prefToggleButton.Value = v; });
            input.CreateFunction("Save Settings", Color.green, () => prefs.SaveToFile());
        }

        public override void OnUpdate()
        {
            try
            {
                HandleToggleInput();
                if (!enabled) return;

                var rigManager = Player.RigManager;
                var physicsRig = Player.PhysicsRig;
                if (rigManager == null || physicsRig == null) return;

                Transform hand = syncHand
                    ? (syncHand == true && toggleControllerHand == HandChoice.LEFT ? physicsRig.leftHand?.transform : null)
                    : null;

                // Preserve the original SyncHand behavior: use the configured hand for the body.
                if (hand == null)
                {
                    hand = toggleControllerHand == HandChoice.LEFT
                        ? physicsRig.leftHand?.transform
                        : physicsRig.rightHand?.transform;
                }

                if (hand == null) return;

                if (hipsEnabled && physicsRig.m_pelvis != null)
                {
                    ApplyHandRotation(physicsRig.m_pelvis, hand.rotation);
                    ApplySway(physicsRig.m_pelvis);
                }

                if (chestEnabled && physicsRig.m_chest != null)
                    ApplyHandRotation(physicsRig.m_chest, hand.rotation);

                if (waistEnabled && physicsRig.m_spine != null)
                    ApplyHandRotation(physicsRig.m_spine, hand.rotation);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("PelvisHandSync update error: " + ex.Message);
            }
        }

        private void ApplyHandRotation(Transform target, Quaternion desired)
        {
            if (target == null) return;
            float follow = Mathf.Clamp01(Time.deltaTime * 7f);
            target.rotation = Quaternion.Slerp(target.rotation, desired, follow);
        }

        private void ApplySway(Transform pelvis)
        {
            if (!swayEnabled || pelvis == null) return;

            float hp = GetHealthPercent();
            bool lowHpAllowed = !onlyLowHp || hp <= minHp;
            if (!lowHpAllowed) return;

            float angle = Mathf.Sin(Time.time * Mathf.Max(0.01f, swaySpeed)) * swayAmount;
            Vector3 euler = swayType == SwayType.ForwardBack
                ? new Vector3(angle, 0f, 0f)
                : new Vector3(0f, 0f, angle);

            pelvis.rotation = pelvis.rotation * Quaternion.Euler(euler);

            if (hipRagdoll)
                UpdateHipRagdoll(pelvis, hp);
        }

        private float GetHealthPercent()
        {
            try
            {
                object rig = Player.RigManager;
                if (rig == null) return 1f;

                object health = GetMemberValue(rig, "health");
                if (health == null) return 1f;

                float current = GetFloatMember(health, "curr_Health");
                float max = GetFloatMember(health, "max_Health");
                if (max <= 0.0001f) return 1f;
                return Mathf.Clamp01(current / max);
            }
            catch
            {
                return 1f;
            }
        }

        private static object GetMemberValue(object obj, string name)
        {
            Type t = obj.GetType();
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(obj);
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null ? p.GetValue(obj, null) : null;
        }

        private static float GetFloatMember(object obj, string name)
        {
            if (obj == null) return 0f;
            object value = GetMemberValue(obj, name);
            return value is float f ? f : Convert.ToSingle(value);
        }

        private void UpdateHipRagdoll(Transform pelvis, float hp)
        {
            // The standalone-safe implementation uses the existing pelvis transform and never
            // invokes game-private ragdoll methods. Below the trigger, the hip sway is increased
            // and hand-follow is released, producing the intended loose-hip effect without
            // reflection into physics internals.
            if (hp <= ragdollHp)
            {
                if (!ragdollState)
                {
                    ragdollState = true;
                    pelvisBaseCaptured = false;
                }
                if (!pelvisBaseCaptured)
                {
                    pelvisBaseRotation = pelvis.rotation;
                    pelvisBaseCaptured = true;
                }

                float loose = Mathf.Sin(Time.time * Mathf.Max(0.5f, swaySpeed * 1.5f)) * Mathf.Max(swayAmount, 8f);
                pelvis.rotation = pelvisBaseRotation * Quaternion.Euler(loose, 0f, loose * 0.35f);
            }
            else
            {
                ragdollState = false;
                pelvisBaseCaptured = false;
            }
        }

        private void HandleToggleInput()
        {
            bool keyboard = inputMode == InputMode.KEYBOARD || inputMode == InputMode.BOTH;
            bool controller = inputMode == InputMode.CONTROLLER || inputMode == InputMode.BOTH;

            if (keyboard && Input.GetKeyDown(toggleKey))
                ToggleEnabled();

            if (controller)
            {
                bool pressed = IsControllerButtonPressed();
                if (pressed && !wasControllerButtonPressed)
                    ToggleEnabled();
                wasControllerButtonPressed = pressed;
            }
            else
            {
                wasControllerButtonPressed = false;
            }
        }

        private void ToggleEnabled()
        {
            enabled = !enabled;
            prefEnabled.Value = enabled;
            MelonLogger.Msg("Pelvis Hand Sync " + (enabled ? "enabled" : "disabled"));
        }

        private bool IsControllerButtonPressed()
        {
            InputDeviceCharacteristics c = toggleControllerHand == HandChoice.LEFT
                ? InputDeviceCharacteristics.Left
                : InputDeviceCharacteristics.Right;

            var devices = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(c, devices);

            for (int i = 0; i < devices.Count; i++)
            {
                bool pressed;
                if (TryReadControllerButton(devices[i], out pressed) && pressed)
                    return true;
            }
            return false;
        }

        private bool TryReadControllerButton(InputDevice device, out bool pressed)
        {
            pressed = false;
            try
            {
                if (toggleButton == ControllerButton.TRIGGER)
                {
                    float value;
                    return device.TryGetFeatureValue(CommonUsages.trigger, out value) && (pressed = value > 0.5f);
                }
                if (toggleButton == ControllerButton.GRIP)
                {
                    float value;
                    return device.TryGetFeatureValue(CommonUsages.grip, out value) && (pressed = value > 0.5f);
                }
                if (toggleButton == ControllerButton.PRIMARY)
                    return device.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
                return device.TryGetFeatureValue(CommonUsages.secondaryButton, out pressed);
            }
            catch
            {
                return false;
            }
        }
    }
}
