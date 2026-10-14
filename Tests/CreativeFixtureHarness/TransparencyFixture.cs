using System;
using System.Collections.Generic;
using System.IO;
using ClientPlugin.Diagnostics;
using Sandbox.Game.World;
using VRage.Game;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
namespace CreativeFixtureHarness;
public sealed partial class Plugin
{
    private MyParticleEffect transparencyEffect,weatherEffect;private int transparencyStage;private uint opaqueDecal,glassDecal;private Vector3D? transparencyNextCamera,transparencyNextTarget;private double transparencyCameraAt;
    private void ExerciseTransparency(double seconds)
    {
        if(transparencyNextCamera.HasValue&&seconds>=transparencyCameraAt){Camera(transparencyNextCamera.Value,transparencyNextTarget.Value);transparencyNextCamera=null;}
        if(transparencyStage==0&&seconds>=78){
            Camera(new Vector3D(1,6,-17),new Vector3D(0,3,-8));
            MatrixD matrix=MatrixD.CreateTranslation(0,3,-8);Vector3D pos=matrix.Translation;
            if(!MyParticlesManager.TryCreateParticleEffect("Damage_Electrical_Damaged_Antenna",ref matrix,ref pos,uint.MaxValue,out transparencyEffect))throw new InvalidOperationException("Installed electrical particle fixture was unavailable.");
            string material="VulkanFixtureDecal";MyRenderProxy.RegisterDecals(new Dictionary<string,List<MyDecalMaterialDesc>>{{material,new List<MyDecalMaterialDesc>{new MyDecalMaterialDesc{ColorMetalTexture="Textures/Decals/BulletHoleOnMetal_01_cm.dds",AlphamaskTexture="Textures/Decals/BulletHoleOnMetal_01_alphamask.dds",NormalmapTexture="Textures/Decals/BulletHoleOnMetal_01_ng.dds"}}}});
            Matrix decal=Matrix.CreateScale(2,2,1)*Matrix.CreateRotationX((float)Math.PI/2);var topo=new MyDecalTopoData{MatrixBinding=decal,MatrixCurrent=decal,WorldPosition=new Vector3D(0,-1.25,0)};
            uint[] parent=platform.Render.RenderObjectIDs;opaqueDecal=MyRenderProxy.CreateDecal(parent,ref topo,MyDecalFlags.World,material,material,0,100,false,int.MaxValue);
            var glass=platform.GetCubeBlock(new Vector3I(0,2,-4));topo.WorldPosition=glass.Position*2.5+platform.PositionComp.GetPosition();topo.MatrixCurrent=Matrix.CreateScale(1.5f,1.5f,1);
            glassDecal=MyRenderProxy.CreateDecal(parent,ref topo,MyDecalFlags.World|MyDecalFlags.Transparent,material,material,0,100,false,int.MaxValue);
            transparencyStage=1;Report("Phase #4: window glass, copied opaque/transparent decals and stock electrical GPU effect.");
        }
        if(transparencyStage==1&&seconds>=95){Capture("transparency-glass-particles");transparencyNextCamera=new Vector3D(-7,6,-7);transparencyNextTarget=new Vector3D(-7,4,0);transparencyCameraAt=seconds+.5;transparencyStage=2;}
        if(transparencyStage==2&&seconds>=108){Capture("transparency-flare");transparencyNextCamera=new Vector3D(0,3,-8);transparencyNextTarget=new Vector3D(0,3,-10);transparencyCameraAt=seconds+.5;MatrixD matrix=MatrixD.CreateTranslation(0,3,-8);Vector3D pos=matrix.Translation;if(!MyParticlesManager.TryCreateParticleEffect("WM_RainHeavy",ref matrix,ref pos,uint.MaxValue,out weatherEffect))throw new InvalidOperationException("Installed rain particle fixture unavailable.");transparencyStage=3;}
        if(transparencyStage==3&&seconds>=120){Capture("transparency-rain");weatherEffect.Stop();MatrixD matrix=MatrixD.CreateTranslation(0,3,-8);Vector3D pos=matrix.Translation;if(!MyParticlesManager.TryCreateParticleEffect("WM_SnowSlow",ref matrix,ref pos,uint.MaxValue,out weatherEffect))throw new InvalidOperationException("Installed snow particle fixture unavailable.");transparencyStage=4;}
        if(transparencyStage==4&&seconds>=135){Capture("transparency-snow");transparencyEffect.Stop();weatherEffect.Stop();MyRenderProxy.RemoveDecal(opaqueDecal);MyRenderProxy.RemoveDecal(glassDecal,true);transparencyStage=5;}
        if(transparencyStage==5&&seconds>=148){Capture("transparency-after-expiry");File.WriteAllText(Path.Combine(reports,"transparency-status.txt"),VulkanDiagnostics.Report());transparencyStage=6;}
    }
}

