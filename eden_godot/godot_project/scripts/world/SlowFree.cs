using Godot;

namespace EdenClient.World;

/// <summary>Libération étalée d'un sous-arbre lourd (cadavres, effets de sort) : on masque et on détache
/// le nœud, puis on libère ses descendants par petits paquets (feuilles d'abord) — évite le pic de
/// ~60 ms qu'un QueueFree d'un coup provoque (« mini freeze » après un kill).</summary>
public partial class SlowFree : Node
{
    static SlowFree? s_inst;
    readonly Queue<Node> _q = new();

    public static void Free(Node n)
    {
        if (!GodotObject.IsInstanceValid(n)) return;
        var tree = n.GetTree();
        if (tree == null) { n.QueueFree(); return; }
        if (s_inst == null || !GodotObject.IsInstanceValid(s_inst))
        {
            s_inst = new SlowFree { Name = "SlowFree", ProcessMode = ProcessModeEnum.Always };
            tree.Root.AddChild(s_inst);
        }
        if (n is Node3D n3) n3.Visible = false;
        n.GetParent()?.RemoveChild(n);
        void Walk(Node x) { foreach (var c in x.GetChildren()) Walk(c); s_inst._q.Enqueue(x); }
        Walk(n);
    }

    public override void _Process(double delta)
    {
        for (var i = 0; i < 10 && _q.Count > 0; i++)
        {
            var n = _q.Dequeue();
            if (GodotObject.IsInstanceValid(n)) n.Free();
        }
    }
}
