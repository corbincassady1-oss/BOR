using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(BulletOverdriveQuest.BulletOverdriveMod), "Bullet Overdrive", "1.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace BulletOverdriveQuest
{
    public sealed class BulletOverdriveMod : MelonMod
    {
        private static bool enabled = true;

        private static bool damageEnabled = true;
        private static float damageMultiplier = 2.0f;

        private static bool speedEnabled = true;
        private static float speedMultiplier = 2.0f;

        private static bool glowEnabled = true;
        private static float glowLifetime = 0.08f;
        private static float glowWidth = 0.025f;
        private static float glowRed = 1.0f;
        private static float glowGreen = 0.15f;
        private static float glowBlue = 0.02f;
        private static float glowAlpha = 1.0f;

        private static bool sparksEnabled = true;
        private static bool nativeMetalSparksEnabled = true;
        private static int sparkCount = 24;
        private static float sparkLifetime = 0.20f;
        private static float sparkSpeed = 7.0f;
        private static float sparkSize = 0.025f;
        private static float sparkRange = 500.0f;

        private static readonly Dictionary<string, double> originalValues = new Dictionary<string, double>();
        private static readonly List<Delegate> liveDelegates = new List<Delegate>();
        private static Type pageType;
        private static Type unityColorType;
        private static Type unityObjectType;
        private static Type gameObjectType;
        private static Type lineRendererType;
        private static Type materialType;
        private static Type shaderType;
        private static Type particleSystemType;
        private static Type physicsType;
        private static Type rayType;
        private static Type vector3Type;
        private static Type timeType;
        private static Type harmonyType;
        private static readonly Dictionary<int, float> recentBulletImpacts = new Dictionary<int, float>();

        public override void OnInitializeMelon()
        {
            try
            {
                CacheUnityTypes();
                InstallBoneLibHook();
                InstallBulletCollisionHook();
                InstallGunImpactVfxHook();
                BuildBoneMenu();
                MelonLogger.Msg("[Bullet Overdrive] Loaded for Quest/IL2CPP.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Bullet Overdrive] Startup failed: " + ex);
            }
        }

        private static void CacheUnityTypes()
        {
            unityColorType = FindType("UnityEngine.Color");
            unityObjectType = FindType("UnityEngine.Object");
            gameObjectType = FindType("UnityEngine.GameObject");
            lineRendererType = FindType("UnityEngine.LineRenderer");
            materialType = FindType("UnityEngine.Material");
            shaderType = FindType("UnityEngine.Shader");
            particleSystemType = FindType("UnityEngine.ParticleSystem");
            physicsType = FindType("UnityEngine.Physics");
            rayType = FindType("UnityEngine.Ray");
            vector3Type = FindType("UnityEngine.Vector3");
            timeType = FindType("UnityEngine.Time");
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return Type.GetType(fullName + ", UnityEngine", false);
        }

        private static void InstallBoneLibHook()
        {
            var hooking = FindType("BoneLib.Hooking");
            if (hooking == null)
                throw new Exception("BoneLib.Hooking was not found. Install BoneLib 3.2.2 first.");

            var evt = hooking.GetEvent("OnPreFireGun", BindingFlags.Public | BindingFlags.Static);
            if (evt == null)
                throw new Exception("BoneLib.OnPreFireGun was not found.");

            var invoke = evt.EventHandlerType.GetMethod("Invoke");
            var parameter = invoke.GetParameters()[0].ParameterType;
            var callback = typeof(BulletOverdriveMod).GetMethod(
                nameof(OnGunPreFire), BindingFlags.NonPublic | BindingFlags.Static);

            var p = Expression.Parameter(parameter, "gun");
            var body = Expression.Call(callback, Expression.Convert(p, typeof(object)));
            var lambda = Expression.Lambda(evt.EventHandlerType, body, p).Compile();
            liveDelegates.Add(lambda);
            evt.AddEventHandler(null, lambda);
        }

        private static void InstallGunImpactVfxHook()
        {
            try
            {
                var harmony = FindType("HarmonyLib.Harmony");
                if (harmony == null) return;

                var collisionType = FindType("UnityEngine.Collision");
                if (collisionType == null) return;

                var allMethods = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                    })
                    .SelectMany(t =>
                    {
                        try
                        {
                            return t.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                BindingFlags.Public | BindingFlags.NonPublic)
                                .Where(m => m.Name == "ImpactVFX" &&
                                            m.GetParameters().Length == 1 &&
                                            m.GetParameters()[0].ParameterType == collisionType)
                                .ToArray();
                        }
                        catch { return Array.Empty<MethodInfo>(); }
                    })
                    .Distinct()
                    .ToArray();

                if (allMethods.Length == 0)
                {
                    MelonLogger.Warning("[Bullet Overdrive] No native ImpactVFX(Collision) method was found.");
                    return;
                }

                var prefix = typeof(BulletOverdriveMod).GetMethod(
                    nameof(GunImpactVfxPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                var harmonyMethodType = FindType("HarmonyLib.HarmonyMethod");
                if (prefix == null || harmonyMethodType == null) return;

                var hmCtor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                if (hmCtor == null) return;

                var harmonyInstance = Activator.CreateInstance(
                    harmony, new object[] { "OpenAI.BulletOverdrive.NativeImpactVFX" });

                var patch = harmony.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Patch" &&
                        m.GetParameters().Length >= 2 &&
                        typeof(MethodBase).IsAssignableFrom(m.GetParameters()[0].ParameterType));
                if (patch == null) return;

                var prefixMethod = hmCtor.Invoke(new object[] { prefix });
                int hooked = 0;

                foreach (var impact in allMethods)
                {
                    try
                    {
                        var ps = patch.GetParameters();
                        var args = new object[ps.Length];
                        args[0] = impact;
                        args[1] = prefixMethod;
                        for (int i = 2; i < args.Length; i++) args[i] = null;
                        patch.Invoke(harmonyInstance, args);
                        hooked++;
                        MelonLogger.Msg("[Bullet Overdrive] Native ImpactVFX target: " +
                                         impact.DeclaringType.FullName);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning("[Bullet Overdrive] ImpactVFX target failed: " + ex.Message);
                    }
                }

                if (hooked > 0)
                    MelonLogger.Msg("[Bullet Overdrive] Hooked " + hooked +
                                    " native ImpactVFX(Collision) method(s) for orange/metal sparks.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Bullet Overdrive] Native ImpactVFX hook failed: " + ex.Message);
            }
        }

        private static void GunImpactVfxPrefix(object collision)
        {
            if (!enabled || !sparksEnabled || !nativeMetalSparksEnabled || collision == null) return;
            try
            {
                ForceNativeMetalImpactFromCollision(collision);
            }
            catch { }
        }

        private static void ForceNativeMetalImpactFromCollision(object collision)
        {
            try
            {
                var collider = GetMember(collision, "collider");
                if (collider == null) collider = GetMember(collision, "gameObject");
                var targetGo = GetMember(collider, "gameObject") ?? collider;
                if (targetGo == null) return;

                var impactType = FindType("Il2CppSLZ.Marrow.ImpactProperties");
                if (impactType == null) return;

                var impact = GetComponent(targetGo, impactType);
                bool created = false;
                if (impact == null)
                {
                    impact = AddComponent(targetGo, impactType);
                    created = impact != null;
                }
                if (impact == null) return;

                var cardRefType = FindType("Il2CppSLZ.Marrow.Warehouse.DataCardReference`1");
                var cardType = FindType("Il2CppSLZ.Marrow.Warehouse.SurfaceDataCard");
                if (cardRefType == null || cardType == null) return;

                var closed = cardRefType.MakeGenericType(cardType);
                object cardRef = null;
                var ctor = closed.GetConstructor(new[] { typeof(string) });
                if (ctor != null)
                    cardRef = ctor.Invoke(new object[] { "SLZ.Backlot.SurfaceDataCard.Metal" });
                else
                    cardRef = Activator.CreateInstance(closed, new object[] { "SLZ.Backlot.SurfaceDataCard.Metal" });

                if (cardRef == null) return;
                SetMember(impact, "SurfaceDataCard", cardRef);

                var setup = impact.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == "SetupSurfaceData" && x.GetParameters().Length == 0);
                if (setup != null) setup.Invoke(impact, null);

                if (created)
                    InvokeDestroy(impact, Math.Max(0.10f, sparkLifetime + 0.10f));
            }
            catch { }
        }
        private static void InstallBulletCollisionHook()
        {
            try
            {
                // GunModifier uses BoneLib's gun-fire hook; Bullet Overdrive keeps that same
                // fire path, then additionally hooks BONELAB's actual projectile collision so
                // impact VFX are created when the bullet really hits something.
                var harmony = FindType("HarmonyLib.Harmony");
                if (harmony == null)
                {
                    MelonLogger.Warning("[Bullet Overdrive] Harmony was not found; native impact hook disabled.");
                    return;
                }

                var bulletType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                    })
                    .FirstOrDefault(t => t.Name == "Bullet" &&
                        t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Any(m => m.Name == "OnCollisionEnter" && m.GetParameters().Length == 1));

                if (bulletType == null)
                {
                    MelonLogger.Warning("[Bullet Overdrive] BONELAB Bullet.OnCollisionEnter was not found.");
                    return;
                }

                var collision = bulletType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .First(m => m.Name == "OnCollisionEnter" && m.GetParameters().Length == 1);
                var prefix = typeof(BulletOverdriveMod).GetMethod(
                    nameof(BulletCollisionPrefix), BindingFlags.Static | BindingFlags.NonPublic);

                var harmonyInstance = Activator.CreateInstance(harmony, new object[] { "OpenAI.BulletOverdrive" });
                var harmonyMethodType = FindType("HarmonyLib.HarmonyMethod");
                if (harmonyMethodType == null) return;
                var hmCtor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                if (hmCtor == null) return;
                var prefixMethod = hmCtor.Invoke(new object[] { prefix });

                var patch = harmony.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Patch" &&
                        m.GetParameters().Length >= 2 &&
                        typeof(MethodBase).IsAssignableFrom(m.GetParameters()[0].ParameterType));
                if (patch == null) return;

                var ps = patch.GetParameters();
                var args = new object[ps.Length];
                args[0] = collision;
                args[1] = prefixMethod;
                for (int i = 2; i < args.Length; i++) args[i] = null;
                patch.Invoke(harmonyInstance, args);

                MelonLogger.Msg("[Bullet Overdrive] Hooked BONELAB Bullet.OnCollisionEnter for real impact VFX.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Bullet Overdrive] Bullet collision hook failed: " + ex.Message);
            }
        }

        private static void BulletCollisionPrefix(object __instance)
        {
            if (!enabled || !sparksEnabled || !nativeMetalSparksEnabled || __instance == null) return;
            try
            {
                int id = __instance.GetHashCode();
                float now = GetTime();
                if (recentBulletImpacts.TryGetValue(id, out var last) && now - last < 0.05f) return;
                recentBulletImpacts[id] = now;

                var tr = GetMember(__instance, "transform");
                if (tr == null) return;
                var point = GetMember(tr, "position");
                var direction = GetMember(tr, "forward");
                if (point == null || direction == null) return;

                // At OnCollisionEnter the projectile is at the real impact location.
                // A short reverse probe recovers the collider and surface point without
                // fabricating a hit at the gun muzzle.
                var back = MultiplyVector(direction, -0.20f);
                var origin = AddVector(point, back);
                var hit = RaycastForImpact(origin, direction, 0.40f);
                if (hit != null)
                    ForceNativeMetalImpact(hit);
            }
            catch { }
        }

        private static float GetTime()
        {
            try
            {
                var p = timeType?.GetProperty("time", BindingFlags.Public | BindingFlags.Static);
                return p != null ? Convert.ToSingle(p.GetValue(null, null)) : 0f;
            }
            catch { return 0f; }
        }

        private static object RaycastForImpact(object origin, object direction, float distance)
        {
            try
            {
                if (physicsType == null || vector3Type == null) return null;
                foreach (var m in physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(x => x.Name == "Raycast"))
                {
                    var ps = m.GetParameters();
                    if (ps.Length != 4) continue;
                    if (ps[0].ParameterType != vector3Type || ps[1].ParameterType != vector3Type || ps[2].ParameterType != typeof(float)) continue;
                    if (!ps[3].ParameterType.IsByRef) continue;

                    var hitType = ps[3].ParameterType.GetElementType();
                    var args = new object[] { origin, direction, distance, Activator.CreateInstance(hitType) };
                    var result = m.Invoke(null, args);
                    if (result is bool ok && ok) return args[3];
                }
            }
            catch { }
            return null;
        }

        private static void BuildBoneMenu()
        {
            pageType = FindType("BoneLib.BoneMenu.Page");
            if (pageType == null) throw new Exception("BoneMenu Page type was not found.");

            var rootProperty = pageType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static);
            var root = rootProperty.GetValue(null);
            if (root == null) throw new Exception("BoneMenu is not initialized yet.");

            var colorRed = MakeColor(1, 0.08f, 0.02f, 1);
            var colorWhite = MakeColor(1, 1, 1, 1);
            var colorYellow = MakeColor(1, 0.85f, 0.05f, 1);
            var colorCyan = MakeColor(0.1f, 0.9f, 1, 1);

            var page = Invoke(root, "CreatePage", "Bullet Overdrive", colorRed, 0, true);
            AddBool(page, "Enabled", enabled, v => enabled = v);

            var damage = Invoke(page, "CreatePage", "Damage", colorYellow, 0, true);
            AddBool(damage, "Enabled", damageEnabled, v => damageEnabled = v);
            AddFloat(damage, "Multiplier", damageMultiplier, 0.1f, 0.1f, 25f, v => damageMultiplier = v);

            var speed = Invoke(page, "CreatePage", "Bullet Speed", colorCyan, 0, true);
            AddBool(speed, "Enabled", speedEnabled, v => speedEnabled = v);
            AddFloat(speed, "Multiplier", speedMultiplier, 0.1f, 0.1f, 25f, v => speedMultiplier = v);

            var glow = Invoke(page, "CreatePage", "Mid-Flight Glow", colorCyan, 0, true);
            AddBool(glow, "Enabled", glowEnabled, v => glowEnabled = v);
            AddFloat(glow, "Lifetime", glowLifetime, 0.01f, 0.01f, 0.5f, v => glowLifetime = v);
            AddFloat(glow, "Width", glowWidth, 0.001f, 0.001f, 0.10f, v => glowWidth = v);
            var glowColor = Invoke(glow, "CreatePage", "Glow Color", colorWhite, 0, true);
            AddFloat(glowColor, "Red", glowRed, 0.05f, 0f, 1f, v => glowRed = v);
            AddFloat(glowColor, "Green", glowGreen, 0.05f, 0f, 1f, v => glowGreen = v);
            AddFloat(glowColor, "Blue", glowBlue, 0.05f, 0f, 1f, v => glowBlue = v);
            AddFloat(glowColor, "Alpha", glowAlpha, 0.05f, 0.05f, 1f, v => glowAlpha = v);

            var sparks = Invoke(page, "CreatePage", "Impact Sparks", colorYellow, 0, true);
            AddBool(sparks, "Enabled", sparksEnabled, v => sparksEnabled = v);
            AddBool(sparks, "Use Native Metal Sparks", nativeMetalSparksEnabled, v => nativeMetalSparksEnabled = v);
            AddInt(sparks, "Count", sparkCount, 1, 1, 100, v => sparkCount = v);
            AddFloat(sparks, "Lifetime", sparkLifetime, 0.02f, 0.02f, 1f, v => sparkLifetime = v);
            AddFloat(sparks, "Speed", sparkSpeed, 0.5f, 0.5f, 30f, v => sparkSpeed = v);
            AddFloat(sparks, "Size", sparkSize, 0.005f, 0.005f, 0.15f, v => sparkSize = v);
            AddFloat(sparks, "Raycast Range", sparkRange, 10f, 10f, 1000f, v => sparkRange = v);

            var advanced = Invoke(page, "CreatePage", "Advanced", colorWhite, 0, true);
            AddFunction(advanced, "Reapply To Current Guns", ReapplyToCurrentGuns);
        }

        private static object MakeColor(float r, float g, float b, float a)
        {
            if (unityColorType == null) return null;
            var ctor = unityColorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            return ctor != null ? ctor.Invoke(new object[] { r, g, b, a }) : Activator.CreateInstance(unityColorType);
        }

        private static void AddBool(object page, string name, bool value, Action<bool> action)
        {
            var m = FindMenuMethod(page, "CreateBool", 4);
            var del = MakeActionDelegate(m.GetParameters()[3].ParameterType, action);
            liveDelegates.Add(del);
            m.Invoke(page, new object[] { name, MakeColor(1,1,1,1), value, del });
        }

        private static void AddFloat(object page, string name, float value, float increment, float min, float max, Action<float> action)
        {
            var m = FindMenuMethod(page, "CreateFloat", 7);
            var del = MakeActionDelegate(m.GetParameters()[6].ParameterType, action);
            liveDelegates.Add(del);
            m.Invoke(page, new object[] { name, MakeColor(1,1,1,1), value, increment, min, max, del });
        }

        private static void AddInt(object page, string name, int value, int increment, int min, int max, Action<int> action)
        {
            var m = FindMenuMethod(page, "CreateInt", 7);
            var del = MakeActionDelegate(m.GetParameters()[6].ParameterType, action);
            liveDelegates.Add(del);
            m.Invoke(page, new object[] { name, MakeColor(1,1,1,1), value, increment, min, max, del });
        }

        private static void AddFunction(object page, string name, Action action)
        {
            var m = FindMenuMethod(page, "CreateFunction", 3);
            var del = MakeActionDelegate(m.GetParameters()[2].ParameterType, action);
            liveDelegates.Add(del);
            m.Invoke(page, new object[] { name, MakeColor(0.2f,0.8f,1,1), del });
        }

        private static MethodInfo FindMenuMethod(object page, string name, int parameterCount)
        {
            return page.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(x => x.Name == name && x.GetParameters().Length == parameterCount);
        }

        private static Delegate MakeActionDelegate(Type delegateType, Delegate source)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var ps = invoke.GetParameters();
            if (ps.Length == 0)
                return Delegate.CreateDelegate(delegateType, source.Target, source.Method);

            var p = Expression.Parameter(ps[0].ParameterType, "value");
            var target = Expression.Constant(source.Target);
            var call = Expression.Call(target, source.Method, Expression.Convert(p, source.Method.GetParameters()[0].ParameterType));
            return Expression.Lambda(delegateType, call, p).Compile();
        }

        private static object Invoke(object instance, string method, params object[] args)
        {
            var m = instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m.Invoke(instance, args);
        }

        private static void OnGunPreFire(object gun)
        {
            if (!enabled || gun == null) return;

            try
            {
                if (damageEnabled || speedEnabled)
                    TuneGunAndProjectile(gun);

                var spawn = GetMember(gun, "bulletSpawnLoc");
                if (spawn == null)
                    spawn = GetMember(gun, "bullet_spawn");

                if (glowEnabled)
                {
                    ConfigureProjectileGlow(gun);
                    ConfigureLiveProjectileGlow();
                }

                // The real collision hook is responsible for metal impact sparks.
                // Keep the gun-fire path from spawning fake sparks at the muzzle.
                // GunModifier's BoneLib fire-hook pattern is still used for the gun tuning.
                if (sparksEnabled && !nativeMetalSparksEnabled)
                    ScheduleImpactSparks(spawn, gun);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Bullet Overdrive] Shot processing failed: " + ex.Message);
            }
        }

        private static void ReapplyToCurrentGuns()
        {
            try
            {
                var gunType = FindType("Il2CppSLZ.Marrow.Gun");
                if (gunType == null) return;
                var objects = GetAllObjectsOfType(gunType);
                foreach (var gun in objects)
                    TuneGunAndProjectile(gun);
                MelonLogger.Msg("[Bullet Overdrive] Reapplied settings to " + objects.Count + " guns.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Bullet Overdrive] Reapply failed: " + ex.Message);
            }
        }

        private static void TuneGunAndProjectile(object gun)
        {
            ApplyNumericMembers(gun, damageEnabled, damageMultiplier,
                new[] { "bullet_dmg", "damage", "baseDamage", "impactDamage", "attackDamage" },
                speedEnabled, speedMultiplier,
                new[] { "bulletSpeed", "projectileSpeed", "projectileVelocity", "muzzleVelocity" });

            var prefab = GetMember(gun, "bullet_prefab");
            if (prefab == null) prefab = GetMember(gun, "bulletPrefab");
            if (prefab != null)
            {
                ApplyNumericMembers(prefab, damageEnabled, damageMultiplier,
                    new[] { "bullet_dmg", "damage", "baseDamage", "impactDamage", "attackDamage" },
                    speedEnabled, speedMultiplier,
                    new[] { "bulletSpeed", "projectileSpeed", "projectileVelocity", "muzzleVelocity" });
                ApplyComponents(prefab);
            }

            var gameObject = GetMember(gun, "gameObject");
            if (gameObject != null)
            {
                var components = GetComponents(gameObject);
                foreach (var component in components)
                {
                    ApplyNumericMembers(component, damageEnabled, damageMultiplier,
                        new[] { "bullet_dmg", "damage", "baseDamage", "impactDamage", "attackDamage" },
                        speedEnabled, speedMultiplier,
                        new[] { "bulletSpeed", "projectileSpeed", "projectileVelocity", "muzzleVelocity" });
                }
            }
        }

        private static void ApplyComponents(object prefab)
        {
            var components = GetComponents(prefab);
            foreach (var component in components)
            {
                ApplyNumericMembers(component, damageEnabled, damageMultiplier,
                    new[] { "bullet_dmg", "damage", "baseDamage", "impactDamage", "attackDamage" },
                    speedEnabled, speedMultiplier,
                    new[] { "bulletSpeed", "projectileSpeed", "projectileVelocity", "muzzleVelocity" });
            }
        }

        private static void ApplyNumericMembers(object instance, bool damageOn, float damageMul, string[] damageNames,
            bool speedOn, float speedMul, string[] speedNames)
        {
            if (instance == null) return;
            var type = instance.GetType();

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(float) && field.FieldType != typeof(double)) continue;
                var n = field.Name;
                if (damageOn && NameMatches(n, damageNames)) Multiply(instance, field, damageMul);
                else if (speedOn && NameMatches(n, speedNames)) Multiply(instance, field, speedMul);
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.PropertyType != typeof(float) && property.PropertyType != typeof(double)) continue;
                var n = property.Name;
                if (damageOn && NameMatches(n, damageNames)) Multiply(instance, property, damageMul);
                else if (speedOn && NameMatches(n, speedNames)) Multiply(instance, property, speedMul);
            }
        }

        private static bool NameMatches(string name, string[] names)
        {
            return names.Any(x => string.Equals(name, x, StringComparison.OrdinalIgnoreCase));
        }

        private static void Multiply(object instance, FieldInfo field, float multiplier)
        {
            try
            {
                var key = instance.GetHashCode() + ":" + field.DeclaringType.FullName + ":" + field.Name;
                object raw = field.GetValue(instance);
                double current = Convert.ToDouble(raw);
                if (!originalValues.TryGetValue(key, out var original))
                {
                    original = current;
                    originalValues[key] = original;
                }
                var result = original * multiplier;
                field.SetValue(instance, field.FieldType == typeof(float) ? (object)(float)result : result);
            }
            catch { }
        }

        private static void Multiply(object instance, PropertyInfo property, float multiplier)
        {
            try
            {
                var key = instance.GetHashCode() + ":" + property.DeclaringType.FullName + ":" + property.Name;
                object raw = property.GetValue(instance, null);
                double current = Convert.ToDouble(raw);
                if (!originalValues.TryGetValue(key, out var original))
                {
                    original = current;
                    originalValues[key] = original;
                }
                var result = original * multiplier;
                property.SetValue(instance, property.PropertyType == typeof(float) ? (object)(float)result : result, null);
            }
            catch { }
        }

        private static void ConfigureProjectileGlow(object gun)
        {
            try
            {
                var prefab = GetMember(gun, "bullet_prefab");
                if (prefab == null) prefab = GetMember(gun, "bulletPrefab");
                if (prefab != null) ConfigureGlowOnObject(prefab);
            }
            catch { }
        }

        private static void ConfigureLiveProjectileGlow()
        {
            try
            {
                var resources = FindType("UnityEngine.Resources");
                if (resources == null || gameObjectType == null) return;

                var findAll = resources.GetMethod("FindObjectsOfTypeAll", BindingFlags.Public | BindingFlags.Static);
                if (findAll == null) return;

                var arr = findAll.MakeGenericMethod(gameObjectType).Invoke(null, null) as Array;
                if (arr == null) return;

                foreach (var go in arr)
                {
                    if (go == null) continue;
                    var name = GetMember(go, "name") as string;
                    if (string.IsNullOrEmpty(name)) continue;

                    if (name.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0)
                        ConfigureGlowOnObject(go);
                }
            }
            catch { }
        }

        private static void ConfigureGlowOnObject(object target)
        {
            try
            {
                var go = GetMember(target, "gameObject") ?? target;
                var trailType = FindType("UnityEngine.TrailRenderer");
                if (go == null || trailType == null) return;

                var trail = GetComponent(go, trailType) ?? AddComponent(go, trailType);
                if (trail == null) return;

                var c = MakeColor(glowRed, glowGreen, glowBlue, glowAlpha);
                SetMember(trail, "enabled", true);
                SetMember(trail, "emitting", true);
                SetMember(trail, "time", glowLifetime);
                SetMember(trail, "startWidth", glowWidth);
                SetMember(trail, "endWidth", glowWidth * 0.1f);
                SetMember(trail, "minVertexDistance", 0.005f);
                SetMember(trail, "startColor", c);
                SetMember(trail, "endColor", MakeColor(glowRed, glowGreen, glowBlue, glowAlpha * 0.05f));
                SetMember(trail, "numCornerVertices", 2);
                SetMember(trail, "numCapVertices", 2);

                var material = CreateGlowMaterial();
                if (material != null)
                {
                    SetMember(trail, "material", material);
                    SetMember(material, "color", c);
                    SetMaterialColor(material, c);
                }
            }
            catch { }
        }

        private static void SetMaterialColor(object material, object color)
        {
            try
            {
                if (materialType == null || unityColorType == null) return;
                var setColor = materialType.GetMethod("SetColor", new[] { typeof(string), unityColorType });
                if (setColor == null) return;
                setColor.Invoke(material, new object[] { "_Color", color });
                setColor.Invoke(material, new object[] { "_TintColor", color });
                setColor.Invoke(material, new object[] { "_EmissionColor", color });
            }
            catch { }
        }

        private static object CreateGlowMaterial()
        {
            try
            {
                if (materialType == null || shaderType == null) return null;
                var find = shaderType.GetMethod("Find", BindingFlags.Public | BindingFlags.Static);
                var shader = find?.Invoke(null, new object[] { "Particles/Standard Unlit" }) ??
                             find?.Invoke(null, new object[] { "Legacy Shaders/Particles/Alpha Blended Premultiply" }) ??
                             find?.Invoke(null, new object[] { "Unlit/Color" }) ??
                             find?.Invoke(null, new object[] { "Sprites/Default" });
                if (shader == null) return null;

                var mat = Activator.CreateInstance(materialType, new[] { shader });
                var color = MakeColor(1f, 0.18f, 0.02f, 1f);
                SetMember(mat, "color", color);
                return mat;
            }
            catch { return null; }
        }

        private static void ScheduleImpactSparks(object spawnTransform, object gun)
        {
            if (spawnTransform == null || physicsType == null) return;
            try
            {
                var origin = GetMember(spawnTransform, "position");
                var direction = GetMember(spawnTransform, "forward");
                if (origin == null || direction == null) return;

                var raycast = physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == "Raycast" && x.GetParameters().Length >= 3 &&
                                         x.GetParameters()[0].ParameterType == vector3Type &&
                                         x.GetParameters()[1].ParameterType == vector3Type &&
                                         x.GetParameters()[2].ParameterType == typeof(float));
                if (raycast == null) return;

                var args = new object[] { origin, direction, sparkRange, null };
                var parameters = raycast.GetParameters();
                object hit;
                if (parameters.Length == 3)
                    hit = raycast.Invoke(null, new object[] { origin, direction, sparkRange });
                else
                    hit = raycast.Invoke(null, args);

                // Unity's Raycast overloads return bool. For a Hit overload, the hit object is written into args[3].
                if (parameters.Length != 3)
                {
                    if (!(hit is bool ok) || !ok || args[3] == null) return;
                    if (nativeMetalSparksEnabled)
                        ForceNativeMetalImpact(args[3]);
                    else
                        SpawnSparksAtHit(args[3]);
                }
                else
                {
                    // Fall back to a short-lived tracer when no RaycastHit overload was selected.
                    SpawnTracer(origin, direction);
                }
            }
            catch { }
        }

        private static void ForceNativeMetalImpact(object hit)
        {
            try
            {
                var collider = GetMember(hit, "collider");
                var targetGo = GetMember(collider, "gameObject") ?? collider;
                if (targetGo == null) return;

                var impactType = FindType("Il2CppSLZ.Marrow.ImpactProperties");
                if (impactType == null) return;

                var impact = GetComponent(targetGo, impactType) ?? AddComponent(targetGo, impactType);
                if (impact == null) return;

                var cardRefType = FindType("Il2CppSLZ.Marrow.Warehouse.DataCardReference`1");
                var cardType = FindType("Il2CppSLZ.Marrow.Warehouse.SurfaceDataCard");
                if (cardRefType != null && cardType != null)
                {
                    var closed = cardRefType.MakeGenericType(cardType);
                    object cardRef = null;
                    var ctor = closed.GetConstructor(new[] { typeof(string) });
                    if (ctor != null)
                        cardRef = ctor.Invoke(new object[] { "SLZ.Backlot.SurfaceDataCard.Metal" });
                    else
                        cardRef = Activator.CreateInstance(closed, new object[] { "SLZ.Backlot.SurfaceDataCard.Metal" });

                    if (cardRef != null)
                        SetMember(impact, "SurfaceDataCard", cardRef);
                }

                var setup = impact.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == "SetupSurfaceData");
                if (setup != null && setup.GetParameters().Length == 0)
                    setup.Invoke(impact, null);

                InvokeDestroy(impact, Math.Max(0.25f, sparkLifetime + 0.15f));
            }
            catch (Exception ex)
            {
                try { MelonLogger.Warning("[Bullet Overdrive] Native metal impact setup failed: " + ex.Message); } catch { }
                SpawnSparksAtHit(hit);
            }
        }

        private static void SpawnSparksAtHit(object hit)
        {
            var point = GetMember(hit, "point");
            var normal = GetMember(hit, "normal");
            if (point == null) return;

            var go = CreateGameObject("BulletOverdrive_Impact");
            if (go == null) return;

            var tr = GetMember(go, "transform");
            if (tr != null)
            {
                SetMember(tr, "position", point);
                if (normal != null)
                {
                    try
                    {
                        var qType = FindType("UnityEngine.Quaternion");
                        var look = qType?.GetMethod("LookRotation", BindingFlags.Public | BindingFlags.Static);
                        if (look != null) SetMember(tr, "rotation", look.Invoke(null, new[] { normal }));
                    }
                    catch { }
                }
            }

            var ps = AddComponent(go, particleSystemType);
            if (ps == null) return;

            var main = GetMember(ps, "main");
            if (main != null)
            {
                SetMember(main, "loop", false);
                SetMember(main, "playOnAwake", false);
                SetMember(main, "duration", sparkLifetime);
                SetMember(main, "startLifetime", sparkLifetime);
                SetMember(main, "startSpeed", sparkSpeed);
                SetMember(main, "startSize", sparkSize);
                SetMember(main, "maxParticles", sparkCount);
            }

            var emission = GetMember(ps, "emission");
            if (emission != null)
            {
                SetMember(emission, "enabled", true);
                SetMember(emission, "rateOverTime", 0f);
            }

            InvokeMethod(ps, "Emit", sparkCount);
            InvokeDestroy(go, sparkLifetime + 0.1f);
        }

        private static void SpawnTracer(object origin, object direction)
        {
            try
            {
                var go = CreateGameObject("BulletOverdrive_Tracer");
                if (go == null || lineRendererType == null) return;
                var line = AddComponent(go, lineRendererType);
                if (line == null) return;

                SetMember(line, "positionCount", 2);
                SetMember(line, "useWorldSpace", true);
                SetMember(line, "startWidth", glowWidth);
                SetMember(line, "endWidth", glowWidth * 0.15f);

                var end = AddVector(origin, MultiplyVector(direction, 0.18f));
                InvokeMethod(line, "SetPosition", 0, origin);
                InvokeMethod(line, "SetPosition", 1, end);

                var material = CreateGlowMaterial();
                if (material != null) SetMember(line, "material", material);
                InvokeDestroy(go, glowLifetime);
            }
            catch { }
        }

        private static object AddVector(object a, object b)
        {
            var op = vector3Type?.GetMethod("op_Addition", BindingFlags.Public | BindingFlags.Static);
            return op?.Invoke(null, new[] { a, b });
        }

        private static object MultiplyVector(object a, float b)
        {
            var op = vector3Type?.GetMethod("op_Multiply", BindingFlags.Public | BindingFlags.Static,
                null, new[] { vector3Type, typeof(float) }, null);
            return op?.Invoke(null, new object[] { a, b });
        }

        private static object CreateGameObject(string name)
        {
            if (gameObjectType == null) return null;
            var ctor = gameObjectType.GetConstructor(new[] { typeof(string) });
            return ctor?.Invoke(new object[] { name });
        }

        private static object AddComponent(object gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null) return null;
            var m = gameObject.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(x => x.Name == "AddComponent" && x.IsGenericMethodDefinition);
            if (m != null) return m.MakeGenericMethod(componentType).Invoke(gameObject, null);

            var nonGeneric = gameObject.GetType().GetMethod("AddComponent", new[] { typeof(Type) });
            return nonGeneric?.Invoke(gameObject, new object[] { componentType });
        }

        private static object GetComponent(object gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null) return null;
            var m = gameObject.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(x => x.Name == "GetComponent" && x.IsGenericMethodDefinition);
            if (m != null) return m.MakeGenericMethod(componentType).Invoke(gameObject, null);

            return gameObject.GetType().GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(gameObject, new object[] { componentType });
        }

        private static List<object> GetComponents(object target)
        {
            var list = new List<object>();
            if (target == null) return list;
            var componentType = FindType("UnityEngine.Component");
            if (componentType == null) return list;

            var method = target.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
            if (method == null)
                method = target.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type) });
            if (method == null) return list;

            object result;
            if (method.GetParameters().Length == 2)
                result = method.Invoke(target, new object[] { componentType, true });
            else
                result = method.Invoke(target, new object[] { componentType });

            if (result is Array arr)
                foreach (var x in arr) if (x != null) list.Add(x);
            return list;
        }

        private static List<object> GetAllObjectsOfType(Type type)
        {
            var list = new List<object>();
            var resources = FindType("UnityEngine.Resources");
            if (resources == null) return list;
            var m = resources.GetMethod("FindObjectsOfTypeAll", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return list;
            var arr = m.MakeGenericMethod(type).Invoke(null, null) as Array;
            if (arr != null) foreach (var x in arr) if (x != null) list.Add(x);
            return list;
        }

        private static object GetMember(object instance, string name)
        {
            if (instance == null) return null;
            var type = instance.GetType();
            try
            {
                var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return p.GetValue(instance, null);
                var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(instance);
            }
            catch { }
            return null;
        }

        private static void SetMember(object instance, string name, object value)
        {
            if (instance == null) return;
            var type = instance.GetType();
            try
            {
                var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite) { p.SetValue(instance, value, null); return; }
                var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) f.SetValue(instance, value);
            }
            catch { }
        }

        private static object InvokeMethod(object instance, string name, params object[] args)
        {
            if (instance == null) return null;
            var methods = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.Name == name && x.GetParameters().Length == args.Length).ToArray();
            if (methods.Length == 0) return null;
            return methods[0].Invoke(instance, args);
        }

        private static void InvokeDestroy(object obj, float delay)
        {
            try
            {
                if (unityObjectType == null) return;
                var methods = unityObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(x => x.Name == "Destroy" && x.GetParameters().Length == 2).ToArray();
                if (methods.Length > 0)
                    methods[0].Invoke(null, new object[] { obj, delay });
            }
            catch { }
        }
    }
}
