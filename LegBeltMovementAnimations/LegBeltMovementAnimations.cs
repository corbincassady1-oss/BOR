using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using BoneLib.BoneMenu;

[assembly: MelonInfo(typeof(LegBeltMovementAnimations.Main), "LegBeltMovementAnimations", "1.0.0", "OpenAI")]
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
        private static MelonPreferences_Entry<float> pIntensity, pSpeed, pLeg, pBelt, pVertical, pSide, pIdle;

        private bool enabled, airborne, turning;
        private AnimationStyle style;
        private float intensity, speed, legAmount, beltAmount, verticalAmount, sideAmount, idleAmount;

        private Transform root, pelvis, belt;
        private Transform leftUpper, rightUpper, leftLower, rightLower, leftFoot, rightFoot;
        private Quaternion pelvisBase, beltBase, leftUpperBase, rightUpperBase, leftLowerBase, rightLowerBase, leftFootBase, rightFootBase;
        private bool basesCaptured;
        private Vector3 lastRootPos;
        private float motion;
        private float turnMotion;
        private float lastYaw;
        private float scanTimer;

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
                pBelt = prefs.CreateEntry("BeltMovement", 1f);
                pVertical = prefs.CreateEntry("VerticalMovement", 1f);
                pSide = prefs.CreateEntry("SideToSideMovement", 1f);
                pIdle = prefs.CreateEntry("IdleMovement", 0.35f);
                pAirborne = prefs.CreateEntry("AirborneMovement", true);
                pTurning = prefs.CreateEntry("TurningMovement", true);

                enabled = pEnabled.Value;
                style = pStyle.Value;
                intensity = Mathf.Clamp(pIntensity.Value, 0f, 2f);
                speed = Mathf.Clamp(pSpeed.Value, .25f, 3f);
                legAmount = Mathf.Clamp(pLeg.Value, 0f, 2f);
                beltAmount = Mathf.Clamp(pBelt.Value, 0f, 2f);
                verticalAmount = Mathf.Clamp(pVertical.Value, 0f, 2f);
                sideAmount = Mathf.Clamp(pSide.Value, 0f, 2f);
                idleAmount = Mathf.Clamp(pIdle.Value, 0f, 2f);
                airborne = pAirborne.Value;
                turning = pTurning.Value;

                BuildMenu();
                MelonLogger.Msg("LegBeltMovementAnimations 1.0.0 initialized.");
            }
            catch (Exception ex) { MelonLogger.Error("LegBeltMovementAnimations init failed: " + ex); }
        }

        private void BuildMenu()
        {
            var page = Page.Root.CreatePage("Leg & Belt Movement", Color.cyan, 0, true);
            page.CreateBool("Enabled", Color.green, enabled, v => { enabled = v; pEnabled.Value = v; });
            page.CreateEnum("Animation Style", Color.cyan, style, v => { style = (AnimationStyle)v; pStyle.Value = style; });
            page.CreateFloat("Intensity", Color.white, intensity, 0f, 2f, .05f, v => { intensity = Mathf.Clamp(v,0f,2f); pIntensity.Value=intensity; });
            page.CreateFloat("Animation Speed", Color.white, speed, .25f, 3f, .05f, v => { speed = Mathf.Clamp(v,.25f,3f); pSpeed.Value=speed; });
            page.CreateFloat("Leg Movement", Color.yellow, legAmount, 0f, 2f, .05f, v => { legAmount=Mathf.Clamp(v,0f,2f); pLeg.Value=legAmount; });
            page.CreateFloat("Hips Movement", Color.yellow, beltAmount, 0f, 2f, .05f, v => { beltAmount=Mathf.Clamp(v,0f,2f); pBelt.Value=beltAmount; });
            page.CreateFloat("Idle Movement", Color.white, idleAmount, 0f, 2f, .05f, v => { idleAmount=Mathf.Clamp(v,0f,2f); pIdle.Value=idleAmount; });
            page.CreateFloat("Vertical Movement", Color.yellow, verticalAmount, 0f, 2f, .05f, v => { verticalAmount=Mathf.Clamp(v,0f,2f); pVertical.Value=verticalAmount; });
            page.CreateFloat("Side-to-Side", Color.yellow, sideAmount, 0f, 2f, .05f, v => { sideAmount=Mathf.Clamp(v,0f,2f); pSide.Value=sideAmount; });
            page.CreateBool("Airborne Movement", Color.white, airborne, v => { airborne=v; pAirborne.Value=v; });
            page.CreateBool("Turning Movement", Color.white, turning, v => { turning=v; pTurning.Value=v; });
            page.CreateFunction("Save Settings", Color.green, () => prefs.SaveToFile());
        }

        public override void OnUpdate()
        {
            try
            {
                if (!enabled) return;
                if (Time.frameCount % 30 == 0 || root == null) FindTargets();
                if (root == null || pelvis == null) return;
                if (!basesCaptured) CaptureBases();

                float dt = Mathf.Max(Time.deltaTime, .0001f);
                Vector3 delta = root.position - lastRootPos;
                float planarSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
                lastRootPos = root.position;

                float move = Mathf.Clamp01(planarSpeed / 1.2f);
                // Keep a subtle animation running while standing still, while preserving full movement response.
                float animationDrive = Mathf.Max(move, idleAmount * 0.22f);
                motion = Mathf.Lerp(motion, move, 1f - Mathf.Exp(-dt * 8f));

                float yaw = root.eulerAngles.y;
                float yawDelta = Mathf.DeltaAngle(lastYaw, yaw) / dt;
                lastYaw = yaw;
                float turn = Mathf.Clamp(yawDelta / 120f, -1f, 1f);
                turnMotion = Mathf.Lerp(turnMotion, turn, 1f - Mathf.Exp(-dt * 10f));

                bool grounded = IsGrounded();
                float airFactor = grounded ? 1f : (airborne ? .45f : 0f);
                if (!grounded && !airborne) motion *= .2f;

                Animate(Time.time * speed, animationDrive * airFactor, turnMotion, move);
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 120 == 0) MelonLogger.Error("LegBeltMovementAnimations update error: " + ex.Message);
            }
        }

        private void Animate(float t, float move, float turn, float actualMove)
        {
            float i = intensity;
            float leg = legAmount * i;
            float beltM = beltAmount * i;
            float vert = verticalAmount * i;
            float side = sideAmount * i;
            float idle = Mathf.Clamp01(1f - actualMove) * idleAmount * i;

            float leftPhase = t;
            float rightPhase = t + Mathf.PI;

            float lPitch = Mathf.Sin(leftPhase) * 28f * leg * move;
            float rPitch = Mathf.Sin(rightPhase) * 28f * leg * move;
            float lRoll = Mathf.Sin(leftPhase * 2f + .7f) * 5f * leg * move;
            float rRoll = Mathf.Sin(rightPhase * 2f + .7f) * 5f * leg * move;

            float beltRoll = Mathf.Sin(t * 1.9f + .35f) * 7f * beltM * move;
            float beltPitch = Mathf.Abs(Mathf.Sin(t)) * 3.5f * beltM * move * vert;
            float beltYaw = Mathf.Sin(t * 1.35f) * 5f * beltM * move;
            float sideBob = Mathf.Sin(t * 2f) * 1.8f * side * move * vert;
            // Idle: gentle alternating leg weight-shift and hip sway when stationary.
            lPitch += Mathf.Sin(t * 1.15f) * 2.5f * leg * idle;
            rPitch += Mathf.Sin(t * 1.15f + Mathf.PI) * 2.5f * leg * idle;
            beltRoll += Mathf.Sin(t * 0.9f) * 2.2f * beltM * idle;
            beltYaw += Mathf.Sin(t * 0.72f + 0.6f) * 1.8f * beltM * idle;
            sideBob += Mathf.Sin(t * 0.82f) * 0.9f * side * idle;

            switch (style)
            {
                case AnimationStyle.Floating:
                    lPitch *= .35f; rPitch *= .35f;
                    beltRoll *= .45f; beltPitch *= .35f; beltYaw *= .65f;
                    sideBob *= .35f;
                    break;
                case AnimationStyle.Girl:
                    lPitch *= .72f; rPitch *= .72f;
                    lRoll += Mathf.Sin(leftPhase) * 4f * leg * move;
                    rRoll += Mathf.Sin(rightPhase) * 4f * leg * move;
                    beltRoll *= 1.7f; beltYaw *= 1.45f; sideBob *= 1.25f;
                    break;
                case AnimationStyle.Sneaky:
                    lPitch *= .58f; rPitch *= .58f;
                    lRoll *= .5f; rRoll *= .5f;
                    beltRoll *= .55f; beltPitch *= .45f; beltYaw *= .65f;
                    sideBob *= .2f;
                    break;
                case AnimationStyle.Bouncy:
                    lPitch *= 1.15f; rPitch *= 1.15f;
                    beltPitch *= 2.2f; beltRoll *= 1.15f;
                    sideBob *= 1.8f;
                    break;
                case AnimationStyle.Robot:
                    lPitch = Mathf.Round(lPitch / 8f) * 8f;
                    rPitch = Mathf.Round(rPitch / 8f) * 8f;
                    beltRoll = Mathf.Round(beltRoll / 3f) * 3f;
                    beltYaw = Mathf.Round(beltYaw / 3f) * 3f;
                    break;
                case AnimationStyle.Heavy:
                    lPitch *= .72f; rPitch *= .72f;
                    beltRoll *= 1.25f; beltPitch *= 1.35f;
                    sideBob *= .75f;
                    break;
                case AnimationStyle.Loose:
                    lPitch *= 1.05f; rPitch *= 1.05f;
                    lRoll *= 1.8f; rRoll *= 1.8f;
                    beltRoll *= 1.8f; beltYaw *= 1.7f;
                    break;
            }

            Apply(leftUpper, leftUpperBase, Quaternion.Euler(lPitch, turn * 5f, lRoll));
            Apply(rightUpper, rightUpperBase, Quaternion.Euler(rPitch, turn * 5f, rRoll));
            Apply(leftLower, leftLowerBase, Quaternion.Euler(-lPitch * .35f, 0f, 0f));
            Apply(rightLower, rightLowerBase, Quaternion.Euler(-rPitch * .35f, 0f, 0f));
            Apply(leftFoot, leftFootBase, Quaternion.Euler(-lPitch * .18f, 0f, -lRoll * .4f));
            Apply(rightFoot, rightFootBase, Quaternion.Euler(-rPitch * .18f, 0f, -rRoll * .4f));

            Apply(belt, beltBase, Quaternion.Euler(beltPitch + sideBob, beltYaw + turn * (turning ? 5f : 0f), beltRoll + turn * (turning ? -4f : 0f)));
            Apply(pelvis, pelvisBase, Quaternion.Euler(beltPitch * .45f, beltYaw * .45f, beltRoll * .5f));
        }

        private void Apply(Transform target, Quaternion baseRot, Quaternion offset)
        {
            if (target == null) return;
            target.localRotation = Quaternion.Slerp(target.localRotation, baseRot * offset, Mathf.Clamp01(Time.deltaTime * 12f));
        }

        private void CaptureBases()
        {
            if (pelvis == null) return;
            pelvisBase = pelvis.localRotation;
            beltBase = belt != null ? belt.localRotation : pelvis.localRotation;
            leftUpperBase = leftUpper != null ? leftUpper.localRotation : Quaternion.identity;
            rightUpperBase = rightUpper != null ? rightUpper.localRotation : Quaternion.identity;
            leftLowerBase = leftLower != null ? leftLower.localRotation : Quaternion.identity;
            rightLowerBase = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            leftFootBase = leftFoot != null ? leftFoot.localRotation : Quaternion.identity;
            rightFootBase = rightFoot != null ? rightFoot.localRotation : Quaternion.identity;
            lastRootPos = root != null ? root.position : Vector3.zero;
            lastYaw = root != null ? root.eulerAngles.y : 0f;
            basesCaptured = true;
        }

        private void FindTargets()
        {
            try
            {
                object rig = GetStaticMember("BoneLib.Player", "RigManager");
                if (rig == null) return;
                root = GetTransform(rig, "transform") ?? FindTransformByNames(rig, new[]{"RigManager","Body","Root"});
                object physics = GetMember(rig, "physicsRig") ?? GetMember(rig, "PhysicsRig");
                Transform source = GetTransform(physics, "transform") ?? root;
                if (source == null) return;

                pelvis = FindNamed(source, new[]{"m_pelvis","Pelvis","pelvis","Hip","Hips"});
                belt = FindNamed(source, new[]{"Waist","waist","Belt","belt","Spine","spine","m_spine"}) ?? pelvis;
                leftUpper = FindNamed(source, new[]{"LeftUpperLeg","LeftThigh","leftUpperLeg","L_Thigh","LeftLeg"});
                rightUpper = FindNamed(source, new[]{"RightUpperLeg","RightThigh","rightUpperLeg","R_Thigh","RightLeg"});
                leftLower = FindNamed(source, new[]{"LeftLowerLeg","LeftShin","leftLowerLeg","L_Shin","LeftCalf"});
                rightLower = FindNamed(source, new[]{"RightLowerLeg","RightShin","rightLowerLeg","R_Shin","RightCalf"});
                leftFoot = FindNamed(source, new[]{"LeftFoot","leftFoot","L_Foot"});
                rightFoot = FindNamed(source, new[]{"RightFoot","rightFoot","R_Foot"});
                if (root == null) root = source;

                bool changed = pelvis != null;
                if (changed && !basesCaptured) CaptureBases();
            }
            catch { }
        }

        private static object GetStaticMember(string typeName, string member)
        {
            Type t = Type.GetType(typeName + ", BoneLib");
            if (t == null) return null;
            var p = t.GetProperty(member, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if (p != null) return p.GetValue(null, null);
            var f = t.GetField(member, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            return f != null ? f.GetValue(null) : null;
        }

        private static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(p!=null)return p.GetValue(obj,null);
            var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            return f!=null?f.GetValue(obj):null;
        }

        private static Transform GetTransform(object obj, string member)
        {
            object v=GetMember(obj,member);
            return v as Transform;
        }

        private static Transform FindTransformByNames(object obj,string[] names)
        {
            Transform t=GetTransform(obj,"transform");
            return t==null?null:FindNamed(t,names);
        }

        private static Transform FindNamed(Transform root,string[] names)
        {
            if(root==null)return null;
            var all=root.GetComponentsInChildren<Transform>(true);
            for(int i=0;i<all.Length;i++)
                for(int j=0;j<names.Length;j++)
                    if(string.Equals(all[i].name,names[j],StringComparison.OrdinalIgnoreCase)) return all[i];
            return null;
        }

        private static bool IsGrounded()
        {
            try
            {
                object rig=GetStaticMember("BoneLib.Player","RigManager");
                object locomotion=GetMember(rig,"locomotion") ?? GetMember(rig,"Locomotion");
                object grounded=GetMember(locomotion,"isGrounded") ?? GetMember(locomotion,"IsGrounded");
                return grounded is bool b ? b : true;
            }
            catch { return true; }
        }
    }
}