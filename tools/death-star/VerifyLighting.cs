using System.IO;
using UnityEngine;
using DeathStar;
using DeathStar.EditorTools;
public static class LightingCompare {
 public static string Run() {
  var s=StationLightingSettings.Current;
  var original=JsonUtility.ToJson(s);
  Directory.CreateDirectory("DeathStarRuns/Lighting");
  try {
   s.screenSpaceGi=false;s.screenSpaceAmbientOcclusion=false;s.shadowSoftness=1;
   TileLab.Run("snap view=Fragment");File.Copy("DeathStarRuns/Tiles/snap.png","DeathStarRuns/Lighting/direct.png",true);
   JsonUtility.FromJsonOverwrite(original,s);
   TileLab.Run("snap view=Fragment");File.Copy("DeathStarRuns/Tiles/snap.png","DeathStarRuns/Lighting/combined.png",true);
   s.giDebugView=GiDebugView.ScreenSpaceGiOnly;
   TileLab.Run("snap view=Fragment");File.Copy("DeathStarRuns/Tiles/snap.png","DeathStarRuns/Lighting/bounce.png",true);
   s.giDebugView=GiDebugView.ScreenSpaceAoOnly;
   TileLab.Run("snap view=Fragment");File.Copy("DeathStarRuns/Tiles/snap.png","DeathStarRuns/Lighting/ao.png",true);
   return "DONE lighting comparisons";
  } finally {JsonUtility.FromJsonOverwrite(original,s);s.ApplyGlobals();}
 }
}
