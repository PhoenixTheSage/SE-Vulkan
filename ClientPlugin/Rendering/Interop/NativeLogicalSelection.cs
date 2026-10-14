using System;
using System.Collections.Generic;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private bool captureLogicalSelection;
    private uint logicalOwner=uint.MaxValue;
    private readonly List<LogicalWorldDraw> logicalDraws=new();
    public void BeginLogicalSelectionCapture(){logicalDraws.Clear();logicalOwner=uint.MaxValue;captureLogicalSelection=true;}
    public LogicalWorldDraw[] EndLogicalSelectionCapture(){captureLogicalSelection=false;logicalOwner=uint.MaxValue;return logicalDraws.ToArray();}
    public LogicalWorldDraw[] PeekLogicalSelectionCapture()=>logicalDraws.ToArray();
    public readonly struct LogicalOwnerScope:IDisposable
    {
        private readonly NativeBackend backend;private readonly uint previous;
        internal LogicalOwnerScope(NativeBackend backend,uint owner){this.backend=backend;previous=backend.logicalOwner;backend.logicalOwner=owner;}
        public void Dispose(){if(backend!=null)backend.logicalOwner=previous;}
    }
    public LogicalOwnerScope WithLogicalOwner(uint owner)=>captureLogicalSelection?new LogicalOwnerScope(this,owner):default;
    private void NoteLogicalDraw(uint pipeline,ulong mesh,uint count,uint flags,uint cascades){if(captureLogicalSelection)logicalDraws.Add(new LogicalWorldDraw{Owner=logicalOwner,Pipeline=pipeline,Mesh=mesh,Count=count,Flags=flags,Cascades=cascades});}
}
public struct LogicalWorldDraw {public uint Owner,Pipeline,Count,Flags,Cascades;public ulong Mesh;}
