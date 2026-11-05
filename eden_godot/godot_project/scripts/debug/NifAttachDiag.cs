using Godot;
using EdenClient.Game;

namespace EdenClient.Debugging;

/// <summary>Sonde headless : attachement skinné des sous-meshes d'une
/// monture (ex. « eye light » de r253). Charge la monture par le vrai
/// chemin <see cref="CharVisual.BuildRideVisual"/> (natif NIF), vérifie
/// que le Skin du mesh résout ses os sur le Skeleton3D, puis skinne en
/// CPU avec la formule de Godot (<c>boneGlobal × bind × v_skin</c>) au
/// repos puis avec la tête pivotée : si le centroïde suit l'os tête,
/// l'attachement est correct.
/// Usage : godot --headless --path . scenes/nif_attach_diag.tscn -- --ride=253010 [--shape=eye light]</summary>
public partial class NifAttachDiag : Node3D
{
    public override void _Ready()
    {
        int rideKey = 253010;
        string wanted = "eye light";
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--ride=") && int.TryParse(a[7..], out var k)) rideKey = k;
            else if (a.StartsWith("--shape=")) wanted = a[8..];
        }
        var mount = CharVisual.BuildRideVisual(rideKey);
        if (mount == null) { GD.PrintErr($"[attachdiag] monture {rideKey} introuvable"); GetTree().Quit(1); return; }
        AddChild(mount);
        var skels = mount.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().ToList();
        GD.Print($"[attachdiag] ride={rideKey} squelettes={skels.Count}");
        foreach (var sk in skels)
            GD.Print($"[attachdiag]   skel '{sk.Name}' os={sk.GetBoneCount()} " +
                     $"tête={(sk.FindBone("Bip01 Head") >= 0 ? "présente" : "ABSENTE")}");
        // Audit global : aucun bind ne doit rester pendant, tout
        // BoneAttachment3D doit viser un os existant.
        var unresolved = 0;
        foreach (var mi in mount.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mi.Skin == null) continue;
            var psk = mi.GetParent() as Skeleton3D;
            for (var b = 0; b < mi.Skin.GetBindCount(); b++)
            {
                var bn = mi.Skin.GetBindName(b).ToString();
                var bi = bn.Length > 0 ? (psk?.FindBone(bn) ?? -1) : mi.Skin.GetBindBone(b);
                if (bi < 0)
                {
                    unresolved++;
                    GD.PrintErr($"[attachdiag] BIND PENDANT mesh='{mi.Name}' bind='{bn}'");
                }
            }
        }
        foreach (var ba in mount.FindChildren("*", "BoneAttachment3D", true, false).Cast<BoneAttachment3D>())
        {
            var psk = ba.GetParent() as Skeleton3D;
            var ok = psk != null && psk.FindBone(ba.BoneName) >= 0;
            GD.Print($"[attachdiag] attachment '{ba.Name}' → os '{ba.BoneName}' {(ok ? "OK" : "NON RÉSOLU")}");
            if (!ok) unresolved++;
        }
        GD.Print($"[attachdiag] binds pendants + attachments cassés : {unresolved}");
        // Cible : mesh skinné dont le nom contient <wanted>
        foreach (var mi in mount.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (!mi.Name.ToString().Contains(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            GD.Print($"[attachdiag] mesh '{mi.Name}' parent='{mi.GetParent()?.Name}' skin={(mi.Skin != null)} " +
                     $"skel='{(mi.Skeleton != null ? mi.GetNodeOrNull(mi.Skeleton)?.Name : null)}' visible={mi.Visible}");
            if (mi.Skin == null || mi.Mesh == null) continue;
            var sk = mi.GetParent() as Skeleton3D ?? skels.FirstOrDefault();
            if (sk == null) { GD.PrintErr("[attachdiag] pas de squelette parent"); continue; }
            var skin = mi.Skin!;
            var binds = new List<(int bone, Transform3D bind)>();
            var ok = true;
            for (var b = 0; b < skin.GetBindCount(); b++)
            {
                var bn = skin.GetBindName(b).ToString();
                var bi = bn.Length > 0 ? sk.FindBone(bn) : skin.GetBindBone(b);
                if (bi < 0) { ok = false; GD.PrintErr($"[attachdiag] bind '{bn}' NON RÉSOLU"); continue; }
                binds.Add((bi, skin.GetBindPose(b)));
            }
            GD.Print($"[attachdiag] binds={binds.Count} résolus={ok}");
            var surf = mi.Mesh.SurfaceGetArrays(0);
            var verts = (Godot.Vector3[])surf[(int)Mesh.ArrayType.Vertex]!;
            var bones = (int[])surf[(int)Mesh.ArrayType.Bones]!;
            var weights = (float[])surf[(int)Mesh.ArrayType.Weights]!;
            Vector3 Centroid()
            {
                var acc = Vector3.Zero;
                for (var i = 0; i < verts.Length; i++)
                {
                    var p = Vector3.Zero;
                    for (var q = 0; q < 4; q++)
                    {
                        var w = weights[i * 4 + q];
                        if (w <= 0) continue;
                        var (bone, bind) = binds[bones[i * 4 + q]];
                        p += (sk.GetBoneGlobalPose(bone) * bind) * verts[i] * w;
                    }
                    acc += p;
                }
                return sk.GlobalTransform * (acc / verts.Length);
            }
            var head = sk.FindBone("Bip01 Head");
            var headW = head >= 0 ? (sk.GlobalTransform * sk.GetBoneGlobalPose(head)).Origin : Vector3.Zero;
            GD.Print($"[attachdiag] repos : œil={Centroid()}  tête={headW}");
            if (head >= 0)
            {
                var rest = sk.GetBonePoseRotation(head);
                sk.SetBonePoseRotation(head, new Quaternion(Vector3.Forward, Mathf.Pi / 4));
                // GetBoneGlobalPose lit la pose courante — pas de notification
                // nécessaire (SetBonePoseRotation invalide le cache interne).
                GD.Print($"[attachdiag] tête+45° : œil={Centroid()}  tête={(sk.GlobalTransform * sk.GetBoneGlobalPose(head)).Origin}");
                sk.SetBonePoseRotation(head, rest);
            }
        }
        GetTree().Quit(0);
    }
}
