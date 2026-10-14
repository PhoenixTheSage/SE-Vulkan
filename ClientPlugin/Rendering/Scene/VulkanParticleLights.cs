using VRage.Render.Scene;

using VRage.Utils;
using VRageMath;
namespace ClientPlugin.Rendering.Scene;
internal sealed partial class VulkanParticles {

	private void UpdateStockLight(StockLight p)
	{
		bool flag = false;
		int num2;
		if ((bool)p.m_data.Enabled && p.Enabled)
		{
			flag = InitParticleLight(p);
			float num = p.m_effect.GetElapsedTime() - p.m_lastVarianceTime;
			if (!(num > (float)p.m_data.VarianceTimeout))
			{
				num2 = ((num < 0f) ? 1 : 0);
				if (num2 == 0)
				{
					goto IL_0074;
				}
			}
			else
			{
				num2 = 1;
			}
			p.m_lastVarianceTime = p.m_effect.GetElapsedTime();
			goto IL_0074;
		}
		CloseParticleLight(p);
		return;
		IL_0074:
		p.m_data.Position.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value);
		if (num2 != 0)
		{
			p.m_data.PositionVar.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value2);
			p.m_localPositionVarRnd = new Vector3(MyUtils.GetRandomFloat(0f - value2.X, value2.X), MyUtils.GetRandomFloat(0f - value2.Y, value2.Y), MyUtils.GetRandomFloat(0f - value2.Z, value2.Z));
		}
		value += p.m_localPositionVarRnd;
		p.m_data.Color.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value3);
		if (num2 != 0)
		{
			p.m_data.ColorVar.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value4);
			p.m_colorVarRnd = MyUtils.GetRandomFloat(1f - value4, 1f + value4);
			value3.X = MathHelper.Clamp(value3.X * p.m_colorVarRnd, 0f, 1f);
			value3.Y = MathHelper.Clamp(value3.Y * p.m_colorVarRnd, 0f, 1f);
			value3.Z = MathHelper.Clamp(value3.Z * p.m_colorVarRnd, 0f, 1f);
		}
		p.m_data.Range.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value5);
		if (num2 != 0)
		{
			p.m_data.RangeVar.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value6);
			p.m_rangeVarRnd = MyUtils.GetRandomFloat(0f - value6, value6);
		}
		value5 += p.m_rangeVarRnd;
		p.m_data.Intensity.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value7);
		if (num2 != 0)
		{
			p.m_data.IntensityVar.GetInterpolatedValue(p.m_effect.GetLoopTime(), out var value8);
			p.m_intensityRnd = MyUtils.GetRandomFloat(0f - value8, value8);
		}
		value7 += p.m_intensityRnd;
		value7 *= p.m_effect.UserBirthMultiplier;
		if (p.m_effect.IsStopped)
		{
			value7 = 0f;
		}
		if (p.m_parentID != p.m_effect.ParentID)
		{
			p.m_parentID = p.m_effect.ParentID;
			IMyActor myActor = MyIDTracker<IMyActor>.FindByID(p.m_parentID);
			while (myActor != null && !myActor.IsRoot() && myActor.Parent != null)
			{
				myActor = myActor.Parent;
			}
		}
		MatrixD worldMatrix = p.m_effect.WorldMatrix;
		Vector3D result = p.m_effect.WorldMatrix.Translation;
		MyActor myActor2;
		if (p.m_parentID != uint.MaxValue && (myActor2 = MyIDTracker<MyActor>.FindByID(p.m_parentID)) != null && myActor2 != null)
		{
			MatrixD matrix = myActor2.WorldMatrix;
			value += (Vector3)worldMatrix.Translation;
			Vector3D.Transform(ref value, ref matrix, out result);
		}
		else
		{
			result = Vector3D.Transform(value * p.m_effect.GetScale(), worldMatrix);
		}
		if (p.m_effect.Gravity.LengthSquared() > 0.0001f)
		{
			Vector3 gravity = p.m_effect.Gravity;
			gravity.Normalize();
			result += gravity * p.m_data.GravityDisplacement;
		}
		bool flag2 = (p.m_position - result).Length() > 0.0010000000474974513;
		bool flag3 = p.m_range != value5;
		bool flag4 = p.m_falloff != (float)p.m_data.Falloff;
		if (flag2 || p.m_color != value3 || flag3 || p.m_intensity != value7 || flag || flag4)
		{
			p.m_color = value3;
			p.m_intensity = value7;
			p.m_range = value5;
			p.m_position = result;
			p.m_falloff = p.m_data.Falloff;
			var data=p.m_renderLightData;
			data.PointLight.Range = value5 * p.m_effect.GetScale();
			data.PointLight.Color = new Vector3(p.m_color) * p.m_intensity;
			data.PointLight.Falloff = p.m_falloff;
			data.Position = p.m_position;
			data.PositionChanged = flag2;
			data.AabbChanged = flag3;
			data.PointIntensity = p.m_intensity;
			p.m_light.GetComponent<VRage.Render.Scene.Components.MyLightComponent>().UpdateData(ref data);p.m_renderLightData=data;
		}
	}

private bool InitParticleLight(StockLight p){if(p.m_light!=null)return false;p.m_light=scene.CreateParticleLight(p.Name);return true;}
private void CloseParticleLight(StockLight p){p.m_light?.Destruct();p.m_light=null;}
}
