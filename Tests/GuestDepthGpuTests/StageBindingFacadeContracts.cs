using System.Reflection;

partial class Program
{
    static int StageBindingFacadeContracts(Assembly core)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        var host=core.GetType("ClientPlugin.Backend.RenderHost",true);var choice=host.GetField("choice",stat);var committed=host.GetField("committed",stat);
        object savedChoice=choice.GetValue(null),savedCommitted=committed.GetValue(null);
        var route=core.GetType("ClientPlugin.Backend.Dx11Call",true);var bound=route.GetField("dx11",stat);object savedAssembly=bound.GetValue(null);
        var facade=core.GetType("ClientPlugin.Shaders.ShaderBindRegistry",true);var request=facade.GetMethod("RequestSrv",stat);
        try
        {
            committed.SetValue(null,false);
            request.Invoke(null,new object[]{"Post.Tonemap","fixture.pending.binding",6});
            var dx11=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(core.Location),"Anomaly.Dx11.dll"));
            bound.SetValue(null,dx11);
            core.GetType("ClientPlugin.Backend.GuestStageBindings",true).GetMethod("BindDx11",stat).Invoke(null,null);
            var dx=dx11.GetType("ClientPlugin.Shaders.ShaderBindRegistry",true);
            bool Has(string name,int slot)
            {
                var model=dx.GetField("Model",stat).GetValue(null);
                return ((System.Collections.IEnumerable)model.GetType().GetMethod("Snapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model,null)).Cast<object>().Any(r=>(string)r.GetType().GetField("CatalogName",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r)==name&&(int)r.GetType().GetField("Slot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r)==slot);
            }
            Check(Has("fixture.pending.binding",6),"pending renderer binding survives into actual DX11 companion");
            committed.SetValue(null,true);choice.SetValue(null,0);
            request.Invoke(null,new object[]{"Post.Tonemap","fixture.active.binding",7});
            Check(Has("fixture.active.binding",7),"shared reflection facade routes active DX11 registration");
            var actual=dx.GetMethod("RequestSrv",stat).GetParameters();var portable=request.GetParameters();
            Check(actual.Length==portable.Length&&actual.Zip(portable).All(p=>p.First.ParameterType==p.Second.ParameterType&&p.First.IsOptional==p.Second.IsOptional&&Equals(p.First.DefaultValue,p.Second.DefaultValue)),"stage-binding reflection signature and optional slot match DX11");
            Check(dx.GetFields(BindingFlags.Static|BindingFlags.Public).Where(f=>f.IsLiteral).All(f=>facade.GetField(f.Name)?.GetRawConstantValue().Equals(f.GetRawConstantValue())==true),"shared stage-binding register constants match DX11");
        }
        finally{choice.SetValue(null,savedChoice);committed.SetValue(null,savedCommitted);bound.SetValue(null,savedAssembly);}
        return checks;
    }
}
