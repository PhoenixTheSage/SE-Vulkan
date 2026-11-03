partial class Program
{
    static object ActorMember(object actor,string name)
        =>actor.GetType().GetField(name)?.GetValue(actor)??actor.GetType().GetProperty(name)!.GetValue(actor);
}
