using System;
using System.Linq;
using System.IO;
using VRage;
using Sandbox.Game.World;
using VRage.Game;
using VRageRender.Messages;
using VRageMath;
using VRageRender;
using ClientPlugin.Diagnostics;
namespace CreativeFixtureHarness;
public sealed partial class Plugin
{
    private int postStage;private uint highlightedActor=uint.MaxValue;
    private void ExercisePostprocessing(double seconds)
    {
        if(Environment.GetEnvironmentVariable("SE_VULKAN_POSTPROCESS_FIXTURE")!="1")return;
        if(postStage==0 && seconds>=8){var settings=MyRenderProxy.Settings;settings.User.ShadowQuality=MyShadowsQuality.HIGH;settings.User.AntialiasingMode=MyAntialiasingMode.FXAA;settings.User.DRScaling=true;MyRenderProxy.SwitchRenderSettings(settings);var post=MyPostprocessSettings.Default;post.EnableEyeAdaptation=true;post.BloomEnabled=true;post.HighQualityBloom=true;post.BloomAntiFlickerFilter=true;post.Data.BloomMult=1;MyRenderProxy.SwitchPostprocessSettings(ref post);var hbao=MyHBAOData.Default;hbao.Enabled=true;MySector.HBAOSettings=hbao;MyRenderProxy.UpdateHBAOSettings(ref hbao);postStage=1;}
        if(postStage==1 && seconds>=16){uint[] ids=door?.Render.RenderObjectIDs??platform.Render.RenderObjectIDs;highlightedActor=ids.FirstOrDefault(x=>x!=uint.MaxValue);MyRenderProxy.UpdateModelHighlight(highlightedActor,null,null,Color.Lime,6,0);postStage=2;}
        if(postStage==2 && seconds>=20){Capture("phase5-highlight");MyRenderProxy.UpdateModelHighlight(highlightedActor,null,null,null,0);var hbao=MyHBAOData.Default;hbao.Enabled=false;MySector.HBAOSettings=hbao;MyRenderProxy.UpdateHBAOSettings(ref hbao);var ssao=MySSAOSettings.Default;ssao.Enabled=true;ssao.UseBlur=true;MySector.SSAOSettings=ssao;MyRenderProxy.UpdateSSAOSettings(ref ssao);postStage=3;}
        if(postStage==3 && seconds>=26){Capture("phase5-ssao");var ssao=MySSAOSettings.Default;ssao.Enabled=false;MySector.SSAOSettings=ssao;MyRenderProxy.UpdateSSAOSettings(ref ssao);var hbao=MyHBAOData.Default;hbao.Enabled=true;MySector.HBAOSettings=hbao;MyRenderProxy.UpdateHBAOSettings(ref hbao);postStage=4;}
        if(postStage==4 && seconds>=44){File.WriteAllText(Path.Combine(reports,"phase5-status.txt"),VulkanDiagnostics.Report());postStage=5;}
        var square=VRage.Utils.MyStringId.GetOrCompute("Square");MyTransparentGeometry.AddPointBillboard(square,new Vector4(1,0,.5f,.5f),new Vector3D(5.4,1.6,-8),.2f,0,blendType:MyBillboard.BlendTypeEnum.LDR);MyTransparentGeometry.AddPointBillboard(square,new Vector4(0,.5f,1,.5f),new Vector3D(5.8,1.6,-8),.2f,0,blendType:MyBillboard.BlendTypeEnum.PostPP);
    }
}
