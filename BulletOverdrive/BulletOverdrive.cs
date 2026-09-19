using System;
using System.Collections.Generic;
using System.Reflection;
using BoneLib;
using BoneLib.BoneMenu;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BulletOverdriveQuest.BulletOverdriveMod), "Bullet Overdrive Quest", "1.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace BulletOverdriveQuest
{
    public class BulletOverdriveMod : MelonMod
    {
        private static bool enabled = true;
        private static bool damageEnabled = true;
        private static float damageMultiplier = 2f;
        private static bool speedEnabled = true;
        private static float speedMultiplier = 2f;
        private static bool tracerEnabled = true;
        private static float tracerLength = 0.18f;
        private static float tracerWidth = 0.012f;
        private static float tracerLifetime = 0.055f;
        private static Color tracerColor = new Color(1f, 0.2f, 0.05f, 1f);
        private static bool sparksEnabled = true;
        private static int sparkCount = 32;
        private static float sparkLifetime = 0.18f;
        private static float sparkSpeed = 7f;
        private static float sparkSize = 0.035f;
        private static float sparkRange = 250f;
        private static bool retuneGuns = true;
        private static readonly HashSet<int> tunedGuns = new HashSet<int>();

        public override void OnInitializeMelon()
        {
            try
            {
                BuildMenu();
                Hooking.OnPostFireGun += OnPostFireGun;
                MelonLogger.Msg("[Bullet Overdrive Quest] Loaded.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Bullet Overdrive Quest] Initialization failed: " + ex);
            }
        }

        private static void BuildMenu()
        {
            Page root = Page.Root.CreatePage("Bullet Overdrive", Color.red, 0, true);
            root.CreateBool("Enabled", Color.white, enabled, v => enabled = v);

            Page damage = root.CreatePage("Damage", Color.yellow, 0, true);
            damage.CreateBool("Enabled", Color.white, damageEnabled, v => damageEnabled = v);
            damage.CreateFloat("Multiplier", Color.yellow, damageMultiplier, 0.1f, 0.1f, 25f, v => damageMultiplier = Mathf.Clamp(v, 0.1f, 25f));

            Page speed = root.CreatePage("Bullet Speed", Color.cyan, 0, true);
            speed.CreateBool("Enabled", Color.white, speedEnabled, v => speedEnabled = v);
            speed.CreateFloat("Multiplier", Color.cyan, speedMultiplier, 0.1f, 0.1f, 25f, v => speedMultiplier = Mathf.Clamp(v, 0.1f, 25f));

            Page glow = root.CreatePage("Mid-Flight Glow", tracerColor, 0, true);
            glow.CreateBool("Enabled", Color.white, tracerEnabled, v => tracerEnabled = v);
            glow.CreateFloat("Length", Color.white, tracerLength, 0.02f, 0.02f, 1f, v => tracerLength = Mathf.Clamp(v, 0.02f, 1f));
            glow.CreateFloat("Width", Color.white, tracerWidth, 0.001f, 0.001f, 0.05f, v => tracerWidth = Mathf.Clamp(v, 0.001f, 0.05f));
            glow.CreateFloat("Lifetime", Color.white, tracerLifetime, 0.01f, 0.01f, 0.5f, v => tracerLifetime = Mathf.Clamp(v, 0.01f, 0.5f));

            Page impact = root.CreatePage("Impact Sparks", Color.yellow, 0, true);
            impact.CreateBool("Enabled", Color.white, sparksEnabled, v => sparksEnabled = v);
            impact.CreateInt("Spark Count", Color.yellow, sparkCount, 1, 1, 100, v => sparkCount = Mathf.Clamp(v, 1, 100));
            impact.CreateFloat("Lifetime", Color.white, sparkLifetime, 0.02f, 0.02f, 1f, v => sparkLifetime = Mathf.Clamp(v, 0.02f, 1f));
            impact.CreateFloat("Speed", Color.white, sparkSpeed, 0.5f, 0.5f, 30f, v => sparkSpeed = Mathf.Clamp(v, 0.5f, 30f));
            impact.CreateFloat("Size", Color.white, sparkSize, 0.005f, 0.005f, 0.15f, v => sparkSize = Mathf.Clamp(v, 0.005f, 0.15f));

            Page advanced = root.CreatePage("Advanced", Color.magenta, 0, true);
            advanced.CreateBool("Retune Guns", Color.white, retuneGuns, v => retuneGuns = v);
            advanced.CreateFunction("Retune All Guns", Color.cyan, () => tunedGuns.Clear());
        }

        private static void OnPostFireGun(Gun gun)
        {
            if (!enabled || gun == null) return;
            try
            {
                if (retuneGuns && (damageEnabled || speedEnabled)) TuneGun(gun);
                if (tracerEnabled) SpawnTracer(gun.transform.position, gun.transform.forward);
                if (sparksEnabled) SpawnImpactSparks(gun.transform.position, gun.transform.forward);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Bullet Overdrive Quest] Shot handling failed: " + ex.Message);
            }
        }

        private static void TuneGun(Gun gun)
        {
            int id = ((Component)gun).GetInstanceID();
            if (!tunedGuns.Add(id)) return;
            Component[] components;
            try { components = gun.gameObject.GetComponentsInChildren<Component>(true); }
            catch { return; }
            if (components == null) return;

            foreach (Component component in components)
            {
                if (component == null) continue;
                Type type = component.GetType();
                if (damageEnabled) MultiplyNumericMembers(component, type, damageMultiplier, new[] { "damage", "baseDamage", "impactDamage" });
                if (speedEnabled) MultiplyNumericMembers(component, type, speedMultiplier, new[] { "projectileSpeed", "bulletSpeed", "muzzleVelocity", "velocity", "speed" });
            }
        }

        private static void MultiplyNumericMembers(Component component, Type type, float multiplier, string[] keywords)
        {
            try
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsNumeric(field.FieldType) || !NameMatches(field.Name, keywords)) continue;
                    try
                    {
                        object value = field.GetValue(component);
                        if (value is float f) field.SetValue(component, f * multiplier);
                        else if (value is double d) field.SetValue(component, d * multiplier);
                    }
                    catch { }
                }

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!property.CanRead || !property.CanWrite || !IsNumeric(property.PropertyType) || !NameMatches(property.Name, keywords)) continue;
                    try
                    {
                        object value = property.GetValue(component, null);
                        if (value is float f) property.SetValue(component, f * multiplier, null);
                        else if (value is double d) property.SetValue(component, d * multiplier, null);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool IsNumeric(Type t) => t == typeof(float) || t == typeof(double);

        private static bool NameMatches(string name, string[] keywords)
        {
            string lower = name.ToLowerInvariant();
            foreach (string keyword in keywords)
                if (lower == keyword || lower.Contains(keyword.ToLowerInvariant())) return true;
            return false;
        }

        private static void SpawnTracer(Vector3 origin, Vector3 direction)
        {
            GameObject go = new GameObject("BulletOverdrive_Tracer");
            try
            {
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = tracerWidth;
                line.endWidth = tracerWidth * 0.15f;
                line.SetPosition(0, origin);
                line.SetPosition(1, origin + direction.normalized * tracerLength);
                Material mat = CreateGlowMaterial();
                if (mat != null) line.material = mat;
                UnityEngine.Object.Destroy(go, tracerLifetime);
            }
            catch { UnityEngine.Object.Destroy(go); }
        }

        private static Material CreateGlowMaterial()
        {
            try
            {
                Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader == null) return null;
                Material material = new Material(shader);
                material.color = tracerColor;
                return material;
            }
            catch { return null; }
        }

        private static void SpawnImpactSparks(Vector3 origin, Vector3 direction)
        {
            try
            {
                RaycastHit hit;
                if (!Physics.Raycast(origin, direction.normalized, out hit, sparkRange)) return;

                GameObject go = new GameObject("BulletOverdrive_Sparks");
                go.transform.position = hit.point + hit.normal * 0.002f;
                go.transform.rotation = Quaternion.LookRotation(hit.normal);

                ParticleSystem ps = go.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.duration = sparkLifetime;
                main.startLifetime = sparkLifetime;
                main.startSpeed = sparkSpeed;
                main.startSize = sparkSize;
                main.maxParticles = sparkCount;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)sparkCount) });

                ParticleSystem.ShapeModule shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35f;
                shape.radius = 0.01f;

                ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
                Material mat = CreateGlowMaterial();
                if (renderer != null && mat != null) renderer.material = mat;
                ps.Play();
                UnityEngine.Object.Destroy(go, sparkLifetime + 0.1f);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Bullet Overdrive Quest] Spark creation failed: " + ex.Message);
            }
        }
    }
}
