using ClientPlugin.Rendering;
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
var scheduler=new ShadowCascadeSchedule();var counts=new int[8];
for(int f=1;f<=32;f++){
    scheduler.BeginFrame();
    for(int c=0;c<8;c++)if(scheduler.IsDue(c,true,false,false,500,0))counts[c]++;
    Check(scheduler.IsDue(4,true,false,false,1001,1000001),"far cascade camera jump overrides interval");
    Check(!scheduler.IsDue(4,true,false,false,1000,1000001) || f%4==3,"near cascade does not get far jump override");
    Check(!scheduler.IsDue(5,true,true,true,1001,1000001),"explicit freeze wins over every-frame and jump");
    Check(scheduler.IsDue(5,true,true,false,0,0),"every-frame setting wins over intervals");
    Check(scheduler.IsDue(5,false,false,true,0,0),"new depth allocation must be initialized");
}
Check(counts.SequenceEqual(new[]{32,32,32,32,8,4,4,4}),"complete stock interval cycles");
scheduler.Reset();
for(int f=1;f<=21;f++){scheduler.BeginFrame();Check(scheduler.IsUsed(0,true,true,0)==(f<=20),"initial usage grace expires after twenty frames");}
Check(scheduler.IsUsed(0,true,true,10),"ten pixels restores used cascade");
for(int f=1;f<=20;f++){scheduler.BeginFrame();Check(scheduler.IsUsed(0,true,true,0),"used cascade restarts twenty-frame grace");}
scheduler.BeginFrame();Check(!scheduler.IsUsed(0,true,true,9),"nine pixels can skip after grace");
Check(scheduler.IsUsed(0,true,false,0),"missing feedback is conservative");
Check(scheduler.IsUsed(0,false,true,0),"usage-skip setting off keeps view");
Console.WriteLine($"PASS {checks} stock scheduling checks");
