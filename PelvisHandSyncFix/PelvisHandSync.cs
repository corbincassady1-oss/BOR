using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;
using BoneLib;
using BoneLib.BoneMenu;

[assembly: MelonInfo(typeof(PelvisHandSync.Main), "PelvisHandSync", "1.3.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

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
        private static MelonPreferences_Entry<bool> prefEnabled, prefSyncHand, prefHipsEnabled, prefChestEnabled, prefWaistEnabled, prefSwayEnabled, prefOnlyLowHp, prefHipRagdoll;
        private static MelonPreferences_Entry<InputMode> prefInputMode;
        private static MelonPreferences_Entry<KeyCode> prefToggleKey;
        private static MelonPreferences_Entry<HandChoice> prefSyncHandChoice, prefToggleControllerHand;
        private static MelonPreferences_Entry<ControllerButton> prefToggleButton;
        private static MelonPreferences_Entry<SwayType> prefSwayType;
        private static MelonPreferences_Entry<float> prefSwaySpeed, prefSwayAmount, prefMinHp, prefRagdollHp;

        private static bool enabled, syncHand, hipsEnabled, chestEnabled, waistEnabled, swayEnabled, onlyLowHp, hipRagdoll, wasControllerButtonPressed;
        private static HandChoice syncHandChoice, toggleControllerHand;
        private static InputMode inputMode;
        private static KeyCode toggleKey;
        private static ControllerButton toggleButton;
        private static SwayType swayType;
        private static float swaySpeed, swayAmount, minHp, ragdollHp;

        private Quaternion pelvisBaseRotation = Quaternion.identity;
        private bool pelvisBaseCaptured, ragdollState;

        public override void OnInitializeMelon()
        {
            try
            {
                prefs = MelonPreferences.CreateCategory(CategoryName);
                prefEnabled = prefs.CreateEntry("Enabled", true);
                prefSyncHand = prefs.CreateEntry("SyncHand", true);
                prefSyncHandChoice = prefs.CreateEntry("SyncHandChoice", HandChoice.RIGHT);
                prefInputMode = prefs.CreateEntry("InputMode", InputMode.MENU_ONLY);
                prefToggleKey = prefs.CreateEntry("ToggleKey", KeyCode.F8);
                prefToggleControllerHand = prefs.CreateEntry("ToggleControllerHand", HandChoice.RIGHT);
                prefToggleButton = prefs.CreateEntry("ToggleButton", ControllerButton.PRIMARY);
                prefHipsEnabled = prefs.CreateEntry("HipsEnabled", true);
                prefChestEnabled = prefs.CreateEntry("ChestEnabled", true);
                prefWaistEnabled = prefs.CreateEntry("WaistEnabled", true);
                prefSwayEnabled = prefs.CreateEntry("SwayingAnimations", true);
                prefSwayType = prefs.CreateEntry("SwayType", SwayType.ForwardBack);
                prefSwaySpeed = prefs.CreateEntry("SwaySpeed", 3f);
                prefSwayAmount = prefs.CreateEntry("SwayAmount", 6f);
                prefOnlyLowHp = prefs.CreateEntry("OnlyAtLowHP", false);
                prefMinHp = prefs.CreateEntry("MinHP", 0.10f);
                prefHipRagdoll = prefs.CreateEntry("HipRagdollBelowHP", false);
                prefRagdollHp = prefs.CreateEntry("RagdollTriggerHP", 0.50f);

                enabled = prefEnabled.Value; syncHand = prefSyncHand.Value; syncHandChoice = prefSyncHandChoice.Value;
                inputMode = prefInputMode.Value; toggleKey = prefToggleKey.Value; toggleControllerHand = prefToggleControllerHand.Value;
                toggleButton = prefToggleButton.Value; hipsEnabled = prefHipsEnabled.Value; chestEnabled = prefChestEnabled.Value;
                waistEnabled = prefWaistEnabled.Value; swayEnabled = prefSwayEnabled.Value; swayType = prefSwayType.Value;
                swaySpeed = Mathf.Clamp(prefSwaySpeed.Value, 1f, 5f); swayAmount = Mathf.Clamp(prefSwayAmount.Value, 0f, 20f);
                onlyLowHp = prefOnlyLowHp.Value; minHp = Mathf.Clamp(prefMinHp.Value, .10f, .50f);
                hipRagdoll = prefHipRagdoll.Value; ragdollHp = Mathf.Clamp(prefRagdollHp.Value, .10f, 1f);

                BuildMenu();
                MelonLogger.Msg("PelvisHandSync 1.3.0 initialized.");
            }
            catch (Exception ex) { MelonLogger.Error("PelvisHandSync initialization failed: " + ex); }
        }

        private void BuildMenu()
        {
            var page = Page.Root.CreatePage("Pelvis Hand Sync", Color.cyan, 0, true);
            page.CreateBool("Enabled", Color.green, enabled, v => { enabled = v; prefEnabled.Value = v; });
            page.CreateBool("Sync To Hand", Color.cyan, syncHand, v => { syncHand = v; prefSyncHand.Value = v; });
            page.CreateEnum("Sync Hand", Color.cyan, syncHandChoice, v => { syncHandChoice = v; prefSyncHandChoice.Value = v; });
            page.CreateBool("Hips", Color.yellow, hipsEnabled, v => { hipsEnabled = v; prefHipsEnabled.Value = v; });
            page.CreateBool("Chest", Color.yellow, chestEnabled, v => { chestEnabled = v; prefChestEnabled.Value = v; });
            page.CreateBool("Waist", Color.yellow, waistEnabled, v => { waistEnabled = v; prefWaistEnabled.Value = v; });

            var sway = page.CreatePage("Swaying Animations", Color.cyan);
            sway.CreateBool("Enabled", Color.green, swayEnabled, v => { swayEnabled = v; prefSwayEnabled.Value = v; });
            sway.CreateEnum("Sway Type", Color.cyan, swayType, v => { swayType = v; prefSwayType.Value = v; });
            sway.CreateFloat("Speed", Color.cyan, swaySpeed, 1f, 5f, .1f, v => { swaySpeed = Mathf.Clamp(v,1f,5f); prefSwaySpeed.Value=swaySpeed; });
            sway.CreateFloat("Amount", Color.cyan, swayAmount, 0f, 20f, .5f, v => { swayAmount = Mathf.Clamp(v,0f,20f); prefSwayAmount.Value=swayAmount; });
            sway.CreateBool("Only At Low HP", Color.yellow, onlyLowHp, v => { onlyLowHp=v; prefOnlyLowHp.Value=v; });
            sway.CreateFloat("Minimum HP", Color.yellow, minHp*100f, 10f, 50f, 1f, v => { minHp=Mathf.Clamp(v/100f,.10f,.50f); prefMinHp.Value=minHp; });
            sway.CreateBool("Hip Ragdoll Below HP", Color.yellow, hipRagdoll, v => { hipRagdoll=v; prefHipRagdoll.Value=v; });
            sway.CreateFloat("Ragdoll Trigger HP", Color.yellow, ragdollHp*100f, 10f, 100f, 1f, v => { ragdollHp=Mathf.Clamp(v/100f,.10f,1f); prefRagdollHp.Value=ragdollHp; });

            var input = page.CreatePage("Toggle Input", Color.white);
            input.CreateEnum("Input Mode", Color.white, inputMode, v => { inputMode=v; prefInputMode.Value=v; });
            input.CreateEnum("Controller Hand", Color.white, toggleControllerHand, v => { toggleControllerHand=v; prefToggleControllerHand.Value=v; });
            input.CreateEnum("Controller Button", Color.white, toggleButton, v => { toggleButton=v; prefToggleButton.Value=v; });
            input.CreateFunction("Save Settings", Color.green, () => prefs.SaveToFile());
        }

        public override void OnUpdate()
        {
            try
            {
                HandleToggleInput();
                if (!enabled) return;
                var physicsRig = Player.PhysicsRig;
                if (physicsRig == null) return;

                Transform hand = syncHand
                    ? (syncHandChoice == HandChoice.LEFT ? physicsRig.leftHand?.transform : physicsRig.rightHand?.transform)
                    : null;

                if (syncHand && hand != null)
                {
                    if (hipsEnabled && physicsRig.m_pelvis != null) ApplyHandRotation(physicsRig.m_pelvis, hand.rotation);
                    if (chestEnabled && physicsRig.m_chest != null) ApplyHandRotation(physicsRig.m_chest, hand.rotation);
                    if (waistEnabled && physicsRig.m_spine != null) ApplyHandRotation(physicsRig.m_spine, hand.rotation);
                }

                if (hipsEnabled && physicsRig.m_pelvis != null)
                    ApplySway(physicsRig.m_pelvis);
            }
            catch (Exception ex) { MelonLogger.Error("PelvisHandSync update error: " + ex.Message); }
        }

        private static void ApplyHandRotation(Transform target, Quaternion desired)
        {
            if (target == null) return;
            target.rotation = Quaternion.Slerp(target.rotation, desired, Mathf.Clamp01(Time.deltaTime * 7f));
        }

        private void ApplySway(Transform pelvis)
        {
            if (!swayEnabled) return;
            float hp = GetHealthPercent();
            if (onlyLowHp && hp > minHp) return;

            float angle = Mathf.Sin(Time.time * Mathf.Max(.01f, swaySpeed)) * swayAmount;
            Quaternion offset = swayType == SwayType.ForwardBack
                ? Quaternion.Euler(angle,0f,0f)
                : Quaternion.Euler(0f,0f,angle);

            if (hipRagdoll && hp <= ragdollHp)
            {
                if (!ragdollState) { ragdollState=true; pelvisBaseCaptured=false; }
                if (!pelvisBaseCaptured) { pelvisBaseRotation=pelvis.rotation; pelvisBaseCaptured=true; }
                float loose = Mathf.Sin(Time.time * Mathf.Max(.5f,swaySpeed*1.5f))*Mathf.Max(swayAmount,8f);
                pelvis.rotation = pelvisBaseRotation * Quaternion.Euler(loose,0f,loose*.35f);
            }
            else
            {
                ragdollState=false; pelvisBaseCaptured=false;
                pelvis.rotation *= offset;
            }
        }

        private static float GetHealthPercent()
        {
            try
            {
                object rig = Player.RigManager;
                if (rig == null) return 1f;
                object health = GetMemberValue(rig, "health");
                if (health == null) return 1f;
                float current = GetFloatMember(health, "curr_Health");
                float max = GetFloatMember(health, "max_Health");
                return max > .0001f ? Mathf.Clamp01(current/max) : 1f;
            }
            catch { return 1f; }
        }

        private static object GetMemberValue(object obj, string name)
        {
            Type t=obj.GetType();
            var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(f!=null) return f.GetValue(obj);
            var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            return p!=null?p.GetValue(obj,null):null;
        }

        private static float GetFloatMember(object obj,string name)
        {
            object value=GetMemberValue(obj,name);
            return value is float f ? f : Convert.ToSingle(value);
        }

        private void HandleToggleInput()
        {
            bool keyboard=inputMode==InputMode.KEYBOARD||inputMode==InputMode.BOTH;
            bool controller=inputMode==InputMode.CONTROLLER||inputMode==InputMode.BOTH;
            if(keyboard&&Input.GetKeyDown(toggleKey)) ToggleEnabled();
            if(controller)
            {
                bool pressed=IsControllerButtonPressed();
                if(pressed&&!wasControllerButtonPressed) ToggleEnabled();
                wasControllerButtonPressed=pressed;
            }
            else wasControllerButtonPressed=false;
        }

        private void ToggleEnabled()
        {
            enabled=!enabled; prefEnabled.Value=enabled;
            MelonLogger.Msg("Pelvis Hand Sync "+(enabled?"enabled":"disabled"));
        }

        private bool IsControllerButtonPressed()
        {
            InputDeviceCharacteristics c=toggleControllerHand==HandChoice.LEFT?InputDeviceCharacteristics.Left:InputDeviceCharacteristics.Right;
            var devices=new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(c,devices);
            for(int i=0;i<devices.Count;i++){ bool pressed; if(TryReadControllerButton(devices[i],out pressed)&&pressed)return true; }
            return false;
        }

        private bool TryReadControllerButton(InputDevice device,out bool pressed)
        {
            pressed=false;
            try
            {
                if(toggleButton==ControllerButton.TRIGGER){float v;return device.TryGetFeatureValue(CommonUsages.trigger,out v)&&(pressed=v>.5f);}
                if(toggleButton==ControllerButton.GRIP){float v;return device.TryGetFeatureValue(CommonUsages.grip,out v)&&(pressed=v>.5f);}
                if(toggleButton==ControllerButton.PRIMARY)return device.TryGetFeatureValue(CommonUsages.primaryButton,out pressed);
                return device.TryGetFeatureValue(CommonUsages.secondaryButton,out pressed);
            }
            catch{return false;}
        }
    }
}