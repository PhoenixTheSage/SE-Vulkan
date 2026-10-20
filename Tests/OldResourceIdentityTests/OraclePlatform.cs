using System.Reflection;
using System.Reflection.Emit;

static partial class Program
{
    // Test-only bootstrap for MyRenderSettings.Default. All graphics operations
    // throw; only the CPU budget constants required by its cctor are available.
    static void InitializeOraclePlatform()
    {
        var module=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OldIdentityCpuOraclePlatform"),AssemblyBuilderAccess.Run).DefineDynamicModule("Oracle");
        var platformProperty=typeof(VRage.MyVRage).GetProperty("Platform")!;Type platform=platformProperty.PropertyType,render=platform.GetProperty("Render")!.PropertyType;
        object Stub(Type contract,object renderValue=null!){var builder=module.DefineType("Stub"+contract.Name,TypeAttributes.Public|TypeAttributes.Sealed);builder.AddInterfaceImplementation(contract);
            var value=builder.DefineField("Render",render,FieldAttributes.Public|FieldAttributes.Static);
            var methods=contract.GetInterfaces().Concat(new[]{contract}).SelectMany(t=>t.GetMethods()).Distinct().ToArray();
            for(int i=0;i<methods.Length;i++){var source=methods[i];if(source.IsGenericMethodDefinition)throw new NotSupportedException("Unexpected generic oracle platform method");
                var method=builder.DefineMethod("Method"+i,MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.Final|MethodAttributes.NewSlot,source.ReturnType,source.GetParameters().Select(p=>p.ParameterType).ToArray());var il=method.GetILGenerator();
                if(source.Name=="get_Render"){il.Emit(OpCodes.Ldsfld,value);il.Emit(OpCodes.Ret);}
                else if(source.Name=="GetMemoryBudgetForStreamedResources"||source.Name=="GetMemoryBudgetForVoxelTextureArrays"||source.Name=="GetMemoryBudgetForGeneratedTextures"){il.Emit(OpCodes.Ldc_I8,1024L*1024*1024);il.Emit(OpCodes.Ret);}
                else {il.Emit(OpCodes.Ldstr,"Graphics/platform operation forbidden in CPU identity oracle: "+source.Name);il.Emit(OpCodes.Newobj,typeof(NotSupportedException).GetConstructor(new[]{typeof(string)})!);il.Emit(OpCodes.Throw);}
                builder.DefineMethodOverride(method,source);
            }
            Type type=builder.CreateType()!;type.GetField("Render")!.SetValue(null,renderValue);return Activator.CreateInstance(type)!;
        }
        platformProperty.GetSetMethod(true)!.Invoke(null,new[]{Stub(platform,Stub(render))});
    }
}
