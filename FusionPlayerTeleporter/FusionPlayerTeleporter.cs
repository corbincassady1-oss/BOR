using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
[assembly: MelonInfo(typeof(FusionPlayerTeleporter.Mod),"Fusion Player Teleporter","1.1.0","OpenAI")]
[assembly: MelonGame("Stress Level Zero","BONELAB")]
namespace FusionPlayerTeleporter{
public sealed class Mod:MelonMod{
static bool built;static object root,playersPage;static Type np,pid,rig,td,relay,tag,route,channel,page,menu,func,link,color;
const double Miles=10000000000000000000.0, MetersPerMile=1609.344;
public override void OnInitializeMelon(){Cache();}
public override void OnLateInitializeMelon(){Build();}
public override void OnUpdate(){if(!built)Build();}
static Type T(string n){foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){try{var t=a.GetType(n,false);if(t!=null)return t;}catch{}}return null;}
static void Cache(){np=T("LabFusion.Entities.NetworkPlayer");pid=T("LabFusion.Player.PlayerIDManager");rig=T("LabFusion.Data.RigData");td=T("LabFusion.Network.PlayerRepTeleportData");relay=T("LabFusion.Network.MessageRelay");tag=T("LabFusion.Network.NativeMessageTag");route=T("LabFusion.Network.MessageRoute");channel=T("LabFusion.Network.NetworkChannel");page=T("BoneLib.BoneMenu.Page");menu=T("BoneLib.BoneMenu.Menu");func=T("BoneLib.BoneMenu.Elements.FunctionElement");link=T("BoneLib.BoneMenu.Elements.PageLinkElement");color=T("UnityEngine.Color");}
static object P(object o,string n){return o?.GetType().GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)?.GetValue(o,null);}
static object SP(Type t,string n){return t?.GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null,null);}
static bool Ready(){return np!=null&&rig!=null&&td!=null&&relay!=null&&tag!=null&&route!=null&&channel!=null&&page!=null&&menu!=null&&func!=null&&link!=null&&color!=null;}
static void Build(){try{Cache();if(built||!Ready())return;root=page.GetProperty("Root",BindingFlags.Public|BindingFlags.Static)?.GetValue(null,null);if(root==null)return;playersPage=NewPage(root,"Fusion Teleporter",10);if(playersPage==null)return;Fn(playersPage,"Refresh Players",Refresh);Fn(playersPage,"Teleport All To Me",AllHere);Fn(playersPage,"Send All Out Of Map",AllAway);Fn(playersPage,"Status",Status);Link(root,"Fusion Teleporter",playersPage);built=true;Refresh();}catch(Exception e){MelonLogger.Error("[Fusion Player Teleporter] "+e);}}
static object NewPage(object parent,string n,int max){return page.GetConstructor(new[]{page,typeof(string),typeof(int)})?.Invoke(new object[]{parent,n,max});}
static object Col(){return Activator.CreateInstance(color,new object[]{1f,1f,1f,1f});}
static void Add(object p,object e){var et=T("BoneLib.BoneMenu.Elements.Element");p.GetType().GetMethod("Add",new[]{et})?.Invoke(p,new[]{e});}
static void Fn(object p,string n,Action a){var c=func.GetConstructor(new[]{typeof(string),color,typeof(Action)});if(c!=null)Add(p,c.Invoke(new object[]{n,Col(),a}));}
static void Link(object p,string n,object child){var c=link.GetConstructor(new[]{typeof(string),color,typeof(Action)});if(c==null)return;var e=c.Invoke(new object[]{n,Col(),(Action)(()=>menu.GetMethod("OpenPage",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new[]{child}))});link.GetMethod("AssignPage")?.Invoke(e,new[]{child});Add(p,e);}
static IEnumerable<Tuple<byte,string>> Remote(){var ps=np.GetProperty("Players",BindingFlags.Public|BindingFlags.Static)?.GetValue(null,null)as IEnumerable;if(ps==null)yield break;foreach(var x in ps){var ne=P(x,"NetworkEntity");if(P(ne,"IsOwner")is bool own&&own)continue;var id=P(P(x,"PlayerID"),"SmallID");if(id==null)continue;yield return Tuple.Create(Convert.ToByte(id),Convert.ToString(P(x,"Username"))??("Player "+id));}}
static void Refresh(){try{if(playersPage==null)return;playersPage.GetType().GetMethod("RemoveAll")?.Invoke(playersPage,null);Fn(playersPage,"Refresh Players",Refresh);Fn(playersPage,"Teleport All To Me",AllHere);Fn(playersPage,"Send All Out Of Map",AllAway);Fn(playersPage,"Status",Status);foreach(var p in Remote()){var pg=NewPage(playersPage,p.Item2+" ["+p.Item1+"]",8);if(pg==null)continue;Fn(pg,"Teleport To Me",()=>Here(p.Item1,p.Item2));Fn(pg,"Send Out Of Map",()=>Away(p.Item1,p.Item2));Fn(pg,"Refresh Players",Refresh);Link(playersPage,p.Item2,pg);}}catch(Exception e){MelonLogger.Error("[Fusion Player Teleporter] "+e);}}
static object LocalPos(){var r=P(SP(rig,"Refs"),"RigManager");return P(P(r,"physicsRig"),"centerOfPressure") is object c?P(c,"position"):null;}
static object Checkpoint(){return P(P(SP(rig,"Refs"),"RigManager"),"checkpointPosition");}
static object OffsetX(object v,float dx){if(v==null)return null;var t=v.GetType();return Activator.CreateInstance(t,new object[]{Convert.ToSingle(P(v,"x"))+dx,Convert.ToSingle(P(v,"y")),Convert.ToSingle(P(v,"z"))});}
static object AwayPos(){var basePos=Checkpoint()??LocalPos();return OffsetX(basePos,3.0e38f);}
static bool Host(){var n=T("LabFusion.Network.NetworkInfo");return SP(n,"IsHost")is bool b&&b;}
static void Send(byte id,object pos){var d=Activator.CreateInstance(td);td.GetProperty("Position")?.SetValue(d,pos,null);var ch=Enum.Parse(channel,"Reliable");var rc=route.GetConstructor(new[]{typeof(byte),channel});var r=rc.Invoke(new object[]{id,ch});var f=tag.GetField("PlayerRepTeleport",BindingFlags.Public|BindingFlags.Static);var tg=f!=null?Convert.ToByte(f.GetValue(null)):Convert.ToByte(tag.GetProperty("PlayerRepTeleport",BindingFlags.Public|BindingFlags.Static).GetValue(null,null));var m=relay.GetMethods(BindingFlags.Public|BindingFlags.Static).First(x=>x.Name=="RelayNative"&&x.IsGenericMethodDefinition&&x.GetGenericArguments().Length==1);m.MakeGenericMethod(td).Invoke(null,new object[]{d,tg,r});}
static void Here(byte id,string n){try{Send(id,LocalPos());MelonLogger.Msg("[Fusion Player Teleporter] Teleport request sent to "+n+". If Fusion accepts only server-originated teleports, the server will determine whether it is applied.");}catch(Exception e){MelonLogger.Error("[Fusion Player Teleporter] "+e);}}
static void Away(byte id,string n){try{Send(id,AwayPos());MelonLogger.Msg("[Fusion Player Teleporter] Teleport request sent to "+n+".");}catch(Exception e){MelonLogger.Error("[Fusion Player Teleporter] "+e);}}
static void AllHere(){try{var p=LocalPos();foreach(var x in Remote())Send(x.Item1,p);}catch(Exception e){MelonLogger.Error("[Fusion Player Teleporter] "+e);}}
static void AllAway(){try{var p=AwayPos();foreach(var x in Remote())Send(x.Item1,p);}catch(Exception e){MelonLogger.Error("[Fusion Player Teleporter] "+e);}}
static void Status(){MelonLogger.Msg("[Fusion Player Teleporter] Fusion 1.14.2 | "+(Host()?"HOST":"CLIENT")+" | host authority is enforced by Fusion itself.");}
}}

// Build marker: Fusion Player Teleporter 1.1.0
