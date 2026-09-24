using System;
using System.Reflection;
using MelonLoader;
using HarmonyLib;

[assembly: MelonInfo(typeof(PelvisHealthSway.Mod), "Pelvis Hand Sync Health Sway", "2.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace PelvisHealthSway {
 public class Mod : MelonMod {
  static HarmonyLib.Harmony harmony;
  public override void OnInitializeMelon() {
   try {
    harmony=new HarmonyLib.Harmony("OpenAI.PelvisHandSync.HealthSway");
    Type t=FindType("PelvisHandSync.Main")??FindType("PelvisHandSync.PelvisHandSync");
    if(t!=null){MethodInfo u=t.GetMethod("OnUpdate",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);MethodInfo p=typeof(Mod).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic);if(u!=null)harmony.Patch(u,postfix:new HarmonyMethod(p));}
    HealthSway.Initialize(); MelonLogger.Msg("[Pelvis Health Sway] Loaded.");
   } catch(Exception e){MelonLogger.Error("[Pelvis Health Sway] "+e);}
  }
  static void Postfix(object __instance)=>HealthSway.Tick(__instance);
  static Type FindType(string n){foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){try{var t=a.GetType(n,false);if(t!=null)return t;}catch{}}return null;}
 }
 internal static class HealthSway {
  static bool initialized; static object swayEl,lowEl,minEl,typeEl,speedEl,ragEl,ragHpEl,lastPelvis,originalLocalPos; static bool hasPos,ragdolled;
  public static void Initialize(){if(initialized)return;initialized=true;try{BuildMenu();}catch{}}
  public static void Tick(object self){
   try{
    if(!initialized)Initialize();
    bool sway=ReadBool(swayEl,true),low=ReadBool(lowEl,false),hip=ReadBool(ragEl,false);
    float min=Clamp(ReadFloat(minEl,.10f),.10f,.50f),speed=Clamp(ReadFloat(speedEl,3f),1f,5f),ragHp=Clamp(ReadFloat(ragHpEl,.50f),.10f,.50f); int typ=ReadEnum(typeEl,0);
    object rig=StaticMember("BoneLib.Player","RigManager");if(rig==null)return;object health=Member(rig,"health");if(health==null)return;
    float cur=Num(Member(health,"curr_Health")),max=Num(Member(health,"max_Health")),hp=max>0?cur/max:1f;
    object pr=StaticMember("BoneLib.Player","PhysicsRig");if(pr==null)return;
    if(hip){if(!ragdolled&&hp<=ragHp){Call(pr,"RagdollRig");ragdolled=true;}else if(ragdolled&&hp>ragHp){Call(pr,"UnRagdollRig");ragdolled=false;}}else if(ragdolled){Call(pr,"UnRagdollRig");ragdolled=false;}
    if(!sway||(low&&hp>min))return;
    object pelvis=Member(pr,"m_pelvis"),tr=Member(pelvis,"transform");if(tr==null)return;
    if(!ReferenceEquals(lastPelvis,tr)){lastPelvis=tr;originalLocalPos=Member(tr,"localPosition");hasPos=originalLocalPos!=null;}if(!hasPos)return;
    float amount=Convert.ToSingle(Type("UnityEngine.Mathf").GetMethod("Sin",new[]{typeof(float)}).Invoke(null,new object[]{Time()*speed}))*.02f;
    Set(tr,"localPosition",Add(originalLocalPos,Vector3(typ==1?amount:0,0,typ==0?amount:0)));
   }catch{}
  }
  static void BuildMenu(){
   System.Type pt=System.Type.GetType("BoneLib.BoneMenu.Page, BoneLib");if(pt==null)return;object root=StaticMember("BoneLib.BoneMenu.Page","Root");if(root==null)return;Type ct=Type("UnityEngine.Color");
   object white=Activator.CreateInstance(ct,new object[]{1f,1f,1f,1f});MethodInfo cp=pt.GetMethod("CreatePage",new[]{typeof(string),ct,typeof(int),typeof(bool)});if(cp==null)return;
   object p=cp.Invoke(root,new object[]{"Pelvis Health Sway",white,0,true});if(p==null)return;
   swayEl=Bool(p,"Sway Animation Enabled",true);lowEl=Bool(p,"Only At Low Health",false);minEl=Float(p,"Minimum HP",.10f,.10f,.50f);typeEl=EnumEl(p,"Sway Type",0);speedEl=Float(p,"Sway Speed",3f,1f,5f);ragEl=Bool(p,"Hip Ragdoll",false);ragHpEl=Float(p,"Ragdoll Trigger HP",.50f,.10f,.50f);
  }
  static object Bool(object p,string n,bool v){MethodInfo m=FindMethod(p.GetType(),"CreateBool",4);return m?.Invoke(p,new object[]{n,Color(.2f,1f,.2f),v,null});}
  static object Float(object p,string n,float v,float a,float b){MethodInfo m=FindMethod(p.GetType(),"CreateFloat",6);return m?.Invoke(p,new object[]{n,Color(1f,1f,.2f),v,a,b,null});}
  static object EnumEl(object p,string n,int v){MethodInfo m=FindMethod(p.GetType(),"CreateEnum",4);return m?.Invoke(p,new object[]{n,Color(1f,1f,1f),(Enum)Enum.ToObject(typeof(SwayChoice),v),null});}
  enum SwayChoice{Fb,Ss}
  static MethodInfo FindMethod(Type t,string n,int c){foreach(var m in t.GetMethods())if(m.Name==n&&m.GetParameters().Length==c)return m;return null;}
  static bool ReadBool(object e,bool d){object v=Member(e,"Value");try{return v==null?d:Convert.ToBoolean(v);}catch{return d;}}
  static float ReadFloat(object e,float d){object v=Member(e,"Value");try{return v==null?d:Convert.ToSingle(v);}catch{return d;}}
  static int ReadEnum(object e,int d){object v=Member(e,"Value");try{return v==null?d:Convert.ToInt32(v);}catch{return d;}}
  static float Clamp(float v,float a,float b)=>v<a?a:v>b?b:v;
  static Type Type(string n)=>System.Type.GetType(n+", UnityEngine.CoreModule")??System.Type.GetType(n+", UnityEngine");
  static object StaticMember(string tn,string n){Type t=System.Type.GetType(tn+", BoneLib")??FindType(tn);return t?.GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null);}
  static Type FindType(string n){foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){try{var t=a.GetType(n,false);if(t!=null)return t;}catch{}}return null;}
  static object Member(object o,string n){if(o==null)return null;Type t=o.GetType();var p=t.GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);if(p!=null)return p.GetValue(o);var f=t.GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);return f?.GetValue(o);}
  static void Set(object o,string n,object v){if(o==null)return;Type t=o.GetType();var p=t.GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);if(p!=null){p.SetValue(o,v);return;}t.GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(o,v);}
  static object Call(object o,string n)=>o?.GetType().GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.Invoke(o,null);
  static float Time()=>Convert.ToSingle(Type("UnityEngine.Time").GetProperty("time",BindingFlags.Public|BindingFlags.Static).GetValue(null));
  static object Vector3(float x,float y,float z)=>Activator.CreateInstance(Type("UnityEngine.Vector3"),new object[]{x,y,z});
  static object Add(object a,object b)=>a.GetType().GetMethod("op_Addition",BindingFlags.Public|BindingFlags.Static).Invoke(null,new[]{a,b});
  static object Color(float r,float g,float b)=>Activator.CreateInstance(Type("UnityEngine.Color"),new object[]{r,g,b,1f});
  static float Num(object o){try{return Convert.ToSingle(o);}catch{return 0;}}
 }
}