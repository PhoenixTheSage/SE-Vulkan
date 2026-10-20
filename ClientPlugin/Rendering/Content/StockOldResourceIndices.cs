using System;
using System.Collections.Generic;

namespace ClientPlugin.Rendering.Content;

/// <summary>
/// Index allocation semantics of MyFreelist for the shared OLD LOD-mesh pool.
/// File LODs, runtime meshes and voxel cells must use the same owner instance;
/// their indices cannot be inferred from Vulkan uploads or actor creation.
/// Pool lifetime/reset remains with that owner, not with scene draw traversal.
/// </summary>
internal sealed class StockOldResourceIndices
{
    readonly List<bool> allocated=new();
    readonly Stack<int> released=new();
    public int Count {get;private set;}

    public int Allocate()
    {
        int index;
        if(released.Count!=0){index=released.Pop();allocated[index]=true;}
        else{index=allocated.Count;allocated.Add(true);}
        Count++;return index;
    }

    public void Free(int index)
    {
        if(index<0||index>=allocated.Count)throw new ArgumentOutOfRangeException(nameof(index));
        if(!allocated[index])throw new ArgumentException("The element is already free.",nameof(index));
        allocated[index]=false;released.Push(index);Count--;
    }

    public void Clear(){allocated.Clear();released.Clear();Count=0;}
}
