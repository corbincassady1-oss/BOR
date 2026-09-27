using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(FusionPlayerTeleporter.Mod), "Fusion Player Teleporter", "2.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace FusionPlayerTeleporter
{
    public sealed class Mod : MelonMod
    {
        static bool built;
        static bool patched;
        static object root, playersPage;
        static Type np, pid, rig, permissionData, permissionEnum, permissionSender, permissionMessage;
        static Type rigRefs, playerRepUtilities, playerSender;
        static Type page, menu, func, link, color;

        const int TeleportToMe = 4;
        const int SendOutOfMap = 5;

        public override void OnInitializeMelon()
        {
            Cache();
            PatchFusionPermissionCommand();
        }

        public override void OnLateInitializeMelon()
        {
            Build();
        }

        public override void OnUpdate()
        {
            if (!built)
                Build();
        }

        static Type T(string n)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = a.GetType(n, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        static void Cache()
        {
            np = T("LabFusion.Entities.NetworkPlayer");
            pid = T("LabFusion.Player.PlayerIDManager");
            rig = T("LabFusion.Data.RigData");

            permissionData = T("LabFusion.Network.PermissionCommandRequestData");
            permissionEnum = T("LabFusion.Network.PermissionCommandType");
            permissionSender = T("LabFusion.Senders.PermissionSender");
            permissionMessage = T("LabFusion.Network.PermissionCommandRequestMessage");

            rigRefs = T("LabFusion.Data.RigRefs");
            playerRepUtilities = T("LabFusion.Representation.PlayerRepUtilities");
            playerSender = T("LabFusion.Senders.PlayerSender");

            page = T("BoneLib.BoneMenu.Page");
            menu = T("BoneLib.BoneMenu.Menu");
            func = T("BoneLib.BoneMenu.Elements.FunctionElement");
            link = T("BoneLib.BoneMenu.Elements.PageLinkElement");
            color = T("UnityEngine.Color");
        }

        static object P(object o, string n)
        {
            if (o == null) return null;
            var t = o.GetType();
            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p != null) return p.GetValue(o, null);
            var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (f != null) return f.GetValue(o);
            return null;
        }

        static object SP(Type t, string n)
        {
            if (t == null) return null;
            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (p != null) return p.GetValue(null, null);
            var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return f.GetValue(null);
            return null;
        }

        static bool Ready()
        {
            return np != null && pid != null && rig != null &&
                   permissionData != null && permissionEnum != null &&
                   permissionSender != null && permissionMessage != null &&
                   page != null && menu != null && func != null && link != null && color != null;
        }

        static void Build()
        {
            try
            {
                Cache();
                if (built || !Ready()) return;

                root = SP(page, "Root");
                if (root == null) return;

                playersPage = NewPage(root, "Fusion Teleporter", 10);
                if (playersPage == null) return;

                Link(root, "Fusion Teleporter", playersPage);
                built = true;
                Refresh();
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Fusion Player Teleporter] " + e);
            }
        }

        static object NewPage(object parent, string n, int max)
        {
            return page.GetConstructor(new[] { page, typeof(string), typeof(int) })?
                .Invoke(new object[] { parent, n, max });
        }

        static object Col()
        {
            return Activator.CreateInstance(color, new object[] { 1f, 1f, 1f, 1f });
        }

        static void Add(object p, object e)
        {
            var et = T("BoneLib.BoneMenu.Elements.Element");
            var m = p?.GetType().GetMethod("Add", new[] { et });
            m?.Invoke(p, new[] { e });
        }

        static void Fn(object p, string n, Action a)
        {
            var c = func.GetConstructor(new[] { typeof(string), color, typeof(Action) });
            if (c != null)
                Add(p, c.Invoke(new object[] { n, Col(), a }));
        }

        static void Link(object p, string n, object child)
        {
            var c = link.GetConstructor(new[] { typeof(string), color, typeof(Action) });
            if (c == null) return;

            Action open = () =>
            {
                try
                {
                    menu.GetMethod("OpenPage", BindingFlags.Public | BindingFlags.Static)
                        ?.Invoke(null, new[] { child });
                }
                catch { }
            };

            var e = c.Invoke(new object[] { n, Col(), open });
            link.GetMethod("AssignPage")?.Invoke(e, new[] { child });
            Add(p, e);
        }

        static IEnumerable<Tuple<byte, string>> Remote()
        {
            var ps = P(np, "Players") as IEnumerable;
            if (ps == null) yield break;

            foreach (var x in ps)
            {
                var ne = P(x, "NetworkEntity");
                var own = P(ne, "IsOwner");
                if (own is bool b && b) continue;

                var playerId = P(x, "PlayerID");
                var id = P(playerId, "SmallID");
                if (id == null) continue;

                var name = Convert.ToString(P(x, "Username"));
                if (string.IsNullOrWhiteSpace(name)) name = "Player " + id;

                yield return Tuple.Create(Convert.ToByte(id), name);
            }
        }

        static void Refresh()
        {
            try
            {
                if (playersPage == null) return;

                playersPage.GetType().GetMethod("RemoveAll")?.Invoke(playersPage, null);

                Fn(playersPage, "Refresh Players", Refresh);
                Fn(playersPage, "Teleport All To Me", AllHere);
                Fn(playersPage, "Send All Out Of Map", AllAway);

                foreach (var p in Remote())
                {
                    var pg = NewPage(playersPage, p.Item2 + " [" + p.Item1 + "]", 8);
                    if (pg == null) continue;

                    var id = p.Item1;
                    var name = p.Item2;

                    Fn(pg, "Teleport To Me", () => RequestTeleport(id, name));
                    Fn(pg, "Send Out Of Map", () => RequestAway(id, name));
                    Fn(pg, "Refresh Players", Refresh);
                    Link(playersPage, name, pg);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Fusion Player Teleporter] " + e);
            }
        }

        static object GetLocalPosition()
        {
            var refs = SP(rig, "Refs");
            var rm = P(refs, "RigManager");
            var physics = P(rm, "physicsRig");
            var feet = P(physics, "feet");
            var transform = P(feet, "transform");
            return P(transform, "position") ?? P(P(physics, "centerOfPressure"), "position");
        }

        static object GetCheckpointPosition()
        {
            var refs = SP(rig, "Refs");
            var rm = P(refs, "RigManager");
            return P(rm, "checkpointPosition") ?? GetLocalPosition();
        }

        static object OffsetX(object v, float dx)
        {
            if (v == null) return null;

            var t = v.GetType();
            var x = Convert.ToSingle(P(v, "x"));
            var y = Convert.ToSingle(P(v, "y"));
            var z = Convert.ToSingle(P(v, "z"));

            return Activator.CreateInstance(t, new object[] { x + dx, y, z });
        }

        static object AwayPosition()
        {
            // Fusion 1.14.2 uses a 50,000 m world limit; use a large finite
            // coordinate that is actually representable by Unity/Fusion.
            return OffsetX(GetCheckpointPosition(), 49000f);
        }

        static void RequestTeleport(byte target, string name)
        {
            try
            {
                if (Host())
                {
                    SendDirect(target, GetLocalPosition());
                }
                else
                {
                    SendPermissionRequest(TeleportToMe, target);
                }

                MelonLogger.Msg("[Fusion Player Teleporter] Teleport request sent for " + name + ".");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Fusion Player Teleporter] " + e);
            }
        }

        static void RequestAway(byte target, string name)
        {
            try
            {
                if (Host())
                {
                    SendDirect(target, AwayPosition());
                }
                else
                {
                    SendPermissionRequest(SendOutOfMap, target);
                }

                MelonLogger.Msg("[Fusion Player Teleporter] Out-of-map request sent for " + name + ".");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Fusion Player Teleporter] " + e);
            }
        }

        static void AllHere()
        {
            foreach (var p in Remote())
                RequestTeleport(p.Item1, p.Item2);
        }

        static void AllAway()
        {
            foreach (var p in Remote())
                RequestAway(p.Item1, p.Item2);
        }

        static bool Host()
        {
            var n = T("LabFusion.Network.NetworkInfo");
            return SP(n, "IsHost") is bool b && b;
        }

        static void SendDirect(byte id, object pos)
        {
            if (playerSender == null || pos == null) return;

            var m = playerSender.GetMethod(
                "SendPlayerTeleport",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(byte), pos.GetType() },
                null);

            if (m == null)
            {
                foreach (var x in playerSender.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (x.Name == "SendPlayerTeleport" && x.GetParameters().Length == 2)
                    {
                        m = x;
                        break;
                    }
                }
            }

            m?.Invoke(null, new[] { (object)id, pos });
        }

        static void SendPermissionRequest(int typeValue, byte target)
        {
            if (permissionSender == null || permissionEnum == null) return;

            var method = permissionSender.GetMethod(
                "SendPermissionRequest",
                BindingFlags.Public | BindingFlags.Static);

            if (method == null) return;

            var enumValue = Enum.ToObject(permissionEnum, typeValue);
            method.Invoke(null, new object[] { enumValue, target });
        }

        static void PatchFusionPermissionCommand()
        {
            try
            {
                if (patched || permissionMessage == null) return;

                var original = permissionMessage.GetMethod(
                    "OnHandleMessage",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (original == null) return;

                var prefix = typeof(Mod).GetMethod(
                    nameof(PermissionPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                new HarmonyLib.Harmony("com.openai.fusionteleporter").Patch(
                    original,
                    prefix: new HarmonyMethod(prefix));

                patched = true;
                MelonLogger.Msg("[Fusion Player Teleporter] Installed permission-independent teleport relay.");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Fusion Player Teleporter] Patch failed: " + e);
            }
        }

        static bool PermissionPrefix(object received)
        {
            try
            {
                if (!Host()) return true;
                if (permissionData == null) return true;

                var readData = received.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(x => x.Name == "ReadData" && x.IsGenericMethodDefinition);

                if (readData == null) return true;

                var data = readData.MakeGenericMethod(permissionData).Invoke(received, null);
                var typeValue = Convert.ToInt32(P(data, "Type"));
                if (typeValue != TeleportToMe && typeValue != SendOutOfMap)
                    return true;

                var senderObj = P(received, "Sender");
                if (senderObj == null) return false;
                byte sender = Convert.ToByte(senderObj);

                var otherObj = P(data, "OtherPlayer");
                if (otherObj == null) return false;
                byte target = Convert.ToByte(otherObj);

                object destination = typeValue == TeleportToMe
                    ? GetPlayerFeetPosition(sender)
                    : AwayPosition();

                if (destination != null)
                {
                    SendDirect(target, destination);
                }

                return false;
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Fusion Player Teleporter] Server relay error: " + e);
                return true;
            }
        }

        static object GetPlayerFeetPosition(byte smallId)
        {
            try
            {
                var pm = T("LabFusion.Entities.NetworkPlayerManager");
                if (pm == null) return null;

                var tryGet = pm.GetMethod(
                    "TryGetPlayer",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(byte), pm.Assembly.GetType("LabFusion.Entities.NetworkPlayer").MakeByRefType() },
                    null);

                if (tryGet != null)
                {
                    var args = new object[] { smallId, null };
                    if (Convert.ToBoolean(tryGet.Invoke(null, args)))
                        return FeetPosition(P(args[1], "RigRefs"));
                }

                var players = P(np, "Players") as IEnumerable;
                if (players != null)
                {
                    foreach (var p in players)
                    {
                        if (Convert.ToByte(P(P(p, "PlayerID"), "SmallID")) == smallId)
                            return FeetPosition(P(p, "RigRefs"));
                    }
                }
            }
            catch { }

            return null;
        }

        static object FeetPosition(object refs)
        {
            var rm = P(refs, "RigManager");
            var physics = P(rm, "physicsRig");
            var feet = P(physics, "feet");
            var transform = P(feet, "transform");
            return P(transform, "position");
        }

        static void Status()
        {
            MelonLogger.Msg(
                "[Fusion Player Teleporter] v2.0.0 | Fusion 1.14.2 | " +
                (Host() ? "HOST" : "CLIENT") +
                " | Fusion teleport permissions bypassed by the host mod."
            );
        }
    }
}