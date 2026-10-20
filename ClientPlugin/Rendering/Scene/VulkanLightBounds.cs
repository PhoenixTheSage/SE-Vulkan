using System;
using VRage.Render.Scene;
using VRage.Render.Scene.Components;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    private sealed partial class VulkanLightComponent
    {
        MyChildCullTreeData lightCullData;
        bool cullPoint,cullSpot,boundsDirty=true;
        float lastPointRadius=-1,lastPointOffset=-1,lastSpotHeight=-1,lastQuerySize=-1;
        public bool PointOn=>m_data.PointLightOn;
        public bool SpotOn=>m_data.SpotLightOn;
        public VRageRender.Messages.UpdateRenderLightData EffectiveData=>m_data;
        void ResetLightBounds(){boundsDirty=true;lastPointRadius=lastPointOffset=lastSpotHeight=lastQuerySize=-1;}
        public override void Assign(IMyActor owner){base.Assign(owner);Owner.SetVisibility(false);ResetLightBounds();}
        public override void OnVisibilityChange(){base.OnVisibilityChange();if(!IsVisible)ResetLightBounds();}
        void UpdateLightBounds()
        {
            // The installed component keeps authored Data intact, while its
            // effective flags suppress weak lights before spatial collection.
            if(m_data.SpotLightOn&&m_data.SpotIntensity<.001f){m_data.SpotLightOn=false;m_data.Glare.Enabled=false;}
            if(m_data.PointIntensity<.001f){m_data.PointLightOn=false;if(!m_data.SpotLightOn)m_data.Glare.Enabled=false;}
            boundsDirty|=m_data.AabbChanged;
            if(!(PointOn||SpotOn||m_data.Glare.Enabled)||!boundsDirty)return;
            float height=SpotOn?(float)Math.Sqrt(1-m_data.SpotLight.ApertureCos*m_data.SpotLight.ApertureCos)/m_data.SpotLight.ApertureCos*m_data.SpotLight.Light.Range:-1;
            bool glare=m_data.Glare.Enabled;
            if(!Owner.HasLocalAabb||(PointOn&&(Math.Abs(m_data.PointLight.Range-lastPointRadius)>1||Math.Abs(m_data.PointOffset-lastPointOffset)>1))||(glare&&Math.Abs(m_data.Glare.QuerySize-lastQuerySize)>1)||(SpotOn&&Math.Abs(height-lastSpotHeight)>1)){
                var box=BoundingBox.CreateInvalid();
                if(SpotOn){lastSpotHeight=height;float radius=height+1;box.Include(new Vector3(-radius,-radius,0));box.Include(new Vector3(radius,radius,-(m_data.SpotLight.Light.Range+1)));}else lastSpotHeight=-1;
                if(PointOn){lastPointRadius=m_data.PointLight.Range;lastPointOffset=m_data.PointOffset;var offset=Vector3.Forward*m_data.PointOffset;box.Include(new BoundingBox(new Vector3(-(lastPointRadius+1))+offset,new Vector3(lastPointRadius+1)+offset));}else lastPointRadius=-1;
                if(glare){lastQuerySize=m_data.Glare.QuerySize;box.Include(new BoundingBox(new Vector3(-(lastQuerySize+1)),new Vector3(lastQuerySize+1)));}else lastQuerySize=-1;
                Owner.SetLocalAabb(box);
            }
            boundsDirty=false;
        }
        void UpdateLightCullData()
        {
            if((lightCullData!=null||(!PointOn&&!SpotOn))&&cullPoint==PointOn&&cullSpot==SpotOn)return;
            bool point=cullPoint=PointOn,spot=cullSpot=SpotOn;
            lightCullData=point||spot?new MyChildCullTreeData{
                Add=(result,contained)=>{var lights=(CullResults)result;if(point)lights.Points.Add(this);if(spot)lights.Spots.Add(this);},
                Remove=result=>{var lights=(CullResults)result;if(point)lights.Points.Remove(this);if(spot)lights.Spots.Remove(this);},
                DebugColor=()=>Color.Yellow}:null;
            Owner.InvalidateCullTreeData();
        }
        public override MyChildCullTreeData GetCullTreeData()=>lightCullData;
    }
}
