using System;
using MelonLoader;
using UnityEngine;
using BoneLib;
using BoneLib.BoneMenu;

[assembly: MelonInfo(typeof(LegBeltMovementAnimations.Main), "LegBeltMovementAnimations", "1.1.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace LegBeltMovementAnimations
{
    public enum AnimationStyle
    {
        Normal,
        Floating,
        Girl,
        Sneaky,
        Bouncy,
        Robot,
        Heavy,
        Loose
    }

    public class Main : MelonMod
    {
        private const string CategoryName = "LegBeltMovementAnimations";

        private static MelonPreferences_Category prefs;
        private static MelonPreferences_Entry<bool> pEnabled, pAirborne, pTurning;
        private static MelonPreferences_Entry<AnimationStyle> pStyle;
        private static MelonPreferences_Entry<float> pIntensity, pSpeed, pLeg, pHips, pVertical, pSide, pIdle;

        private bool enabled, airborne, turning;
        private AnimationStyle style;
        private float intensity, speed, legAmount, hipsAmount, verticalAmount, sideAmount, idleAmount;

        private Transform rigRoot;
        private Animator avatarAnimator;

        private Transform hips, leftUpper, rightUpper, leftLower, rightLower, leftFoot, rightFoot;
        private Quaternion hipsBase, leftUpperBase, rightUpperBase, leftLowerBase, rightLowerBase, leftFootBase, rightFootBase;
        private bool basesCaptured;
        private int targetScanFrames;
        private Vector3 lastRootPos;
        private float motion;
        private float turnMotion;
        private float lastYaw;
        private bool groundedState = true;
        private int groundedCheckFrames;

        public override void OnInitializeMelon()
        {
            try
            {
                prefs = MelonPreferences.CreateCategory(CategoryName);

                pEnabled = prefs.CreateEntry("Enabled", true);
                pStyle = prefs.CreateEntry("AnimationStyle", AnimationStyle.Normal);
                pIntensity = prefs.CreateEntry("Intensity", 1f);
                pSpeed = prefs.CreateEntry("AnimationSpeed", 1f);
                pLeg = prefs.CreateEntry("LegMovement", 1f);
                pHips = prefs.CreateEntry("BeltMovement", 1f);
                pVertical = prefs.CreateEntry("VerticalMovement", 1f);
                pSide = prefs.CreateEntry("SideToSideMovement", 1f);
                pIdle = prefs.CreateEntry("IdleMovement", 0.35f);
                pAirborne = prefs.CreateEntry("AirborneMovement", true);
                pTurning = prefs.CreateEntry("TurningMovement", true);

                enabled = pEnabled.Value;
                style = Enum.IsDefined(typeof(AnimationStyle), pStyle.Value) ? pStyle.Value : AnimationStyle.Normal;
                intensity = Clamp(pIntensity.Value, 0f, 2f, 1f);
                speed = Clamp(pSpeed.Value, .25f, 3f, 1f);
                legAmount = Clamp(pLeg.Value, 0f, 2f, 1f);
                hipsAmount = Clamp(pHips.Value, 0f, 2f, 1f);
                verticalAmount = Clamp(pVertical.Value, 0f, 2f, 1f);
                sideAmount = Clamp(pSide.Value, 0f, 2f, 1f);
                idleAmount = Clamp(pIdle.Value, 0f, 2f, .35f);
                airborne = pAirborne.Value;
                turning = pTurning.Value;

                BuildMenu();
                MelonLogger.Msg("LegBeltMovementAnimations 1.1.0 initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("LegBeltMovementAnimations init failed: " + ex);
            }
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return Mathf.Clamp(value, min, max);
        }

        private void BuildMenu()
        {
            var rootPage = Page.Root.CreatePage("Leg & Hips Movement", Color.cyan, 0, true);

            var general = rootPage.CreatePage("General", Color.cyan, 0, true);
            general.CreateBool("Enabled", Color.green, enabled, v => { enabled = v; pEnabled.Value = v; });
            general.CreateEnum("Animation Style", Color.cyan, style, v =>
            {
                var selected = v is AnimationStyle ? (AnimationStyle)v : AnimationStyle.Normal;
                style = Enum.IsDefined(typeof(AnimationStyle), selected) ? selected : AnimationStyle.Normal;
                pStyle.Value = style;
            });
            general.CreateFloat("Intensity", Color.white, intensity, .05f, 0f, 2f, v => { intensity = Mathf.Clamp(v, 0f, 2f); pIntensity.Value = intensity; });
            general.CreateFloat("Animation Speed", Color.white, speed, .05f, .25f, 3f, v => { speed = Mathf.Clamp(v, .25f, 3f); pSpeed.Value = speed; });
            general.CreateFloat("Idle Movement", Color.white, idleAmount, .05f, 0f, 2f, v => { idleAmount = Mathf.Clamp(v, 0f, 2f); pIdle.Value = idleAmount; });
            general.CreateBool("Airborne Movement", Color.white, airborne, v => { airborne = v; pAirborne.Value = v; });
            general.CreateBool("Turning Movement", Color.white, turning, v => { turning = v; pTurning.Value = v; });
            general.CreateFunction("Save Settings", Color.green, () => prefs.SaveToFile());

            var body = rootPage.CreatePage("Body Movement", Color.yellow, 0, true);
            body.CreateFloat("Leg Movement", Color.yellow, legAmount, .05f, 0f, 2f, v => { legAmount = Mathf.Clamp(v, 0f, 2f); pLeg.Value = legAmount; });
            body.CreateFloat("Hips Movement", Color.yellow, hipsAmount, .05f, 0f, 2f, v => { hipsAmount = Mathf.Clamp(v, 0f, 2f); pHips.Value = hipsAmount; });
            body.CreateFloat("Vertical Movement", Color.yellow, verticalAmount, .05f, 0f, 2f, v => { verticalAmount = Mathf.Clamp(v, 0f, 2f); pVertical.Value = verticalAmount; });
            body.CreateFloat("Side-to-Side", Color.yellow, sideAmount, .05f, 0f, 2f, v => { sideAmount = Mathf.Clamp(v, 0f, 2f); pSide.Value = sideAmount; });
            body.CreateFunction("Save Settings", Color.green, () => prefs.SaveToFile());
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            ResetTargets();
        }

        public override void OnUpdate()
        {
            if (!enabled)
                return;

            try
            {
                UpdateMotionState();
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 180 == 0)
                    MelonLogger.Error("LegBeltMovementAnimations motion error: " + ex.Message);
            }
        }

        public override void OnLateUpdate()
        {
            if (!enabled)
                return;

            try
            {
                if (rigRoot == null || hips == null || (targetScanFrames++ % 90) == 0)
                    FindTargets();

                if (rigRoot == null || hips == null)
                    return;

                if (!basesCaptured)
                    CaptureBases();

                float airFactor = groundedState ? 1f : (airborne ? .45f : .2f);
                float drive = Mathf.Max(motion, idleAmount * .22f) * airFactor;
                Animate(Time.time * speed, drive, turnMotion, motion);
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 180 == 0)
                    MelonLogger.Error("LegBeltMovementAnimations animation error: " + ex.Message);
            }
        }

        private void UpdateMotionState()
        {
            if (rigRoot == null)
            {
                if (Player.RigManager != null)
                    rigRoot = Player.RigManager.transform;
                else
                    return;

                lastRootPos = rigRoot.position;
                lastYaw = rigRoot.eulerAngles.y;
            }

            float dt = Mathf.Max(Time.deltaTime, .0001f);
            Vector3 delta = rigRoot.position - lastRootPos;
            float planarSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
            lastRootPos = rigRoot.position;

            float move = Mathf.Clamp01(planarSpeed / 1.2f);
            motion = Mathf.Lerp(motion, move, 1f - Mathf.Exp(-dt * 8f));

            float yaw = rigRoot.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            float turn = Mathf.Clamp(yawDelta / 120f, -1f, 1f);
            turnMotion = Mathf.Lerp(turnMotion, turn, 1f - Mathf.Exp(-dt * 10f));

            if ((groundedCheckFrames++ % 6) == 0)
                groundedState = IsGrounded();
        }

        private void Animate(float t, float move, float turn, float actualMove)
        {
            float i = intensity;
            float leg = legAmount * i;
            float hipsM = hipsAmount * i;
            float vert = verticalAmount * i;
            float side = sideAmount * i;
            float idle = Mathf.Clamp01(1f - actualMove) * idleAmount * i;

            float leftPhase = t;
            float rightPhase = t + Mathf.PI;

            float lPitch = Mathf.Sin(leftPhase) * 28f * leg * move;
            float rPitch = Mathf.Sin(rightPhase) * 28f * leg * move;
            float lRoll = Mathf.Sin(leftPhase * 2f + .7f) * 5f * leg * move;
            float rRoll = Mathf.Sin(rightPhase * 2f + .7f) * 5f * leg * move;

            float hipsRoll = Mathf.Sin(t * 1.9f + .35f) * 7f * hipsM * move;
            float hipsPitch = Mathf.Abs(Mathf.Sin(t)) * 3.5f * hipsM * move * vert;
            float hipsYaw = Mathf.Sin(t * 1.35f) * 5f * hipsM * move;
            float sideBob = Mathf.Sin(t * 2f) * 1.8f * side * move * vert;

            lPitch += Mathf.Sin(t * 1.15f) * 2.5f * leg * idle;
            rPitch += Mathf.Sin(t * 1.15f + Mathf.PI) * 2.5f * leg * idle;
            hipsRoll += Mathf.Sin(t * .9f) * 2.2f * hipsM * idle;
            hipsYaw += Mathf.Sin(t * .72f + .6f) * 1.8f * hipsM * idle;
            sideBob += Mathf.Sin(t * .82f) * .9f * side * idle;

            switch (style)
            {
                case AnimationStyle.Floating:
                    lPitch *= .35f; rPitch *= .35f; hipsRoll *= .45f; hipsPitch *= .35f; hipsYaw *= .65f; sideBob *= .35f;
                    break;
                case AnimationStyle.Girl:
                    lPitch *= .72f; rPitch *= .72f;
                    lRoll += Mathf.Sin(leftPhase) * 4f * leg * move;
                    rRoll += Mathf.Sin(rightPhase) * 4f * leg * move;
                    hipsRoll *= 1.7f; hipsYaw *= 1.45f; sideBob *= 1.25f;
                    break;
                case AnimationStyle.Sneaky:
                    lPitch *= .58f; rPitch *= .58f; lRoll *= .5f; rRoll *= .5f;
                    hipsRoll *= .55f; hipsPitch *= .45f; hipsYaw *= .65f; sideBob *= .2f;
                    break;
                case AnimationStyle.Bouncy:
                    lPitch *= 1.15f; rPitch *= 1.15f; hipsPitch *= 2.2f; hipsRoll *= 1.15f; sideBob *= 1.8f;
                    break;
                case AnimationStyle.Robot:
                    lPitch = Mathf.Round(lPitch / 8f) * 8f; rPitch = Mathf.Round(rPitch / 8f) * 8f;
                    hipsRoll = Mathf.Round(hipsRoll / 3f) * 3f; hipsYaw = Mathf.Round(hipsYaw / 3f) * 3f;
                    break;
                case AnimationStyle.Heavy:
                    lPitch *= .72f; rPitch *= .72f; hipsRoll *= 1.25f; hipsPitch *= 1.35f; sideBob *= .75f;
                    break;
                case AnimationStyle.Loose:
                    lPitch *= 1.05f; rPitch *= 1.05f; lRoll *= 1.8f; rRoll *= 1.8f; hipsRoll *= 1.8f; hipsYaw *= 1.7f;
                    break;
            }

            Apply(leftUpper, leftUpperBase, Quaternion.Euler(lPitch, turn * 5f, lRoll));
            Apply(rightUpper, rightUpperBase, Quaternion.Euler(rPitch, turn * 5f, rRoll));
            Apply(leftLower, leftLowerBase, Quaternion.Euler(-lPitch * .35f, 0f, 0f));
            Apply(rightLower, rightLowerBase, Quaternion.Euler(-rPitch * .35f, 0f, 0f));
            Apply(leftFoot, leftFootBase, Quaternion.Euler(-lPitch * .18f, 0f, -lRoll * .4f));
            Apply(rightFoot, rightFootBase, Quaternion.Euler(-rPitch * .18f, 0f, -rRoll * .4f));

            Apply(hips, hipsBase, Quaternion.Euler(hipsPitch + sideBob, hipsYaw + turn * (turning ? 5f : 0f), hipsRoll + turn * (turning ? -4f : 0f)));
        }

        private static void Apply(Transform target, Quaternion baseRot, Quaternion offset)
        {
            if (target == null)
                return;

            target.localRotation = baseRot * offset;
        }

        private void CaptureBases()
        {
            if (hips == null)
                return;

            hipsBase = hips.localRotation;
            leftUpperBase = leftUpper != null ? leftUpper.localRotation : Quaternion.identity;
            rightUpperBase = rightUpper != null ? rightUpper.localRotation : Quaternion.identity;
            leftLowerBase = leftLower != null ? leftLower.localRotation : Quaternion.identity;
            rightLowerBase = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            leftFootBase = leftFoot != null ? leftFoot.localRotation : Quaternion.identity;
            rightFootBase = rightFoot != null ? rightFoot.localRotation : Quaternion.identity;

            lastRootPos = rigRoot != null ? rigRoot.position : Vector3.zero;
            lastYaw = rigRoot != null ? rigRoot.eulerAngles.y : 0f;
            basesCaptured = true;
        }

        private void FindTargets()
        {
            try
            {
                var manager = Player.RigManager;
                if (manager == null)
                    return;

                rigRoot = manager.transform;

                Animator foundAnimator = null;
                var animators = manager.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < animators.Length; i++)
                {
                    var a = animators[i];
                    if (a == null)
                        continue;

                    if (a.isHuman && a.GetBoneTransform(HumanBodyBones.Hips) != null)
                    {
                        foundAnimator = a;
                        break;
                    }

                    if (foundAnimator == null)
                        foundAnimator = a;
                }

                avatarAnimator = foundAnimator;

                if (avatarAnimator != null && avatarAnimator.isHuman)
                {
                    hips = avatarAnimator.GetBoneTransform(HumanBodyBones.Hips);
                    leftUpper = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    rightUpper = avatarAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    leftLower = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                    rightLower = avatarAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                    leftFoot = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    rightFoot = avatarAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
                }
                else
                {
                    Transform source = manager.transform.Find("PhysicsRig") ?? manager.transform;
                    hips = FindNamed(source, new[] { "Pelvis", "pelvis", "Hips", "hips", "Hip", "hip" });
                    leftUpper = FindNamed(source, new[] { "LeftUpperLeg", "LeftThigh", "leftUpperLeg", "L_Thigh" });
                    rightUpper = FindNamed(source, new[] { "RightUpperLeg", "RightThigh", "rightUpperLeg", "R_Thigh" });
                    leftLower = FindNamed(source, new[] { "LeftLowerLeg", "LeftShin", "leftLowerLeg", "L_Shin" });
                    rightLower = FindNamed(source, new[] { "RightLowerLeg", "RightShin", "rightLowerLeg", "R_Shin" });
                    leftFoot = FindNamed(source, new[] { "LeftFoot", "leftFoot", "L_Foot" });
                    rightFoot = FindNamed(source, new[] { "RightFoot", "rightFoot", "L_Foot" });
                }

                basesCaptured = hips != null;
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 180 == 0)
                    MelonLogger.Warning("LegBeltMovementAnimations target scan failed: " + ex.Message);
            }
        }

        private void ResetTargets()
        {
            rigRoot = null;
            avatarAnimator = null;
            hips = leftUpper = rightUpper = leftLower = rightLower = leftFoot = rightFoot = null;
            basesCaptured = false;
            targetScanFrames = 0;
            motion = 0f;
            turnMotion = 0f;
            groundedState = true;
            groundedCheckFrames = 0;
        }

        private static Transform FindNamed(Transform root, string[] names)
        {
            if (root == null)
                return null;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                for (int j = 0; j < names.Length; j++)
                {
                    if (string.Equals(all[i].name, names[j], StringComparison.OrdinalIgnoreCase))
                        return all[i];
                }
            }

            return null;
        }

        private static bool IsGrounded()
        {
            try
            {
                var physics = Player.PhysicsRig;
                if (physics == null)
                    return true;

                var feet = physics.feet;
                if (feet == null)
                    return true;

                var grounder = feet.GetComponent("PhysGrounder");
                if (grounder == null)
                    return true;

                var field = grounder.GetType().GetField("isGrounded");
                if (field != null && field.FieldType == typeof(bool))
                    return (bool)field.GetValue(grounder);

                var prop = grounder.GetType().GetProperty("isGrounded");
                if (prop != null && prop.PropertyType == typeof(bool))
                    return (bool)prop.GetValue(grounder, null);

                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
