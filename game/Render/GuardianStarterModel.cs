using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Guardians;

namespace Vivarium.Game.Render;

/// <summary>
/// Distinct, lightweight starter silhouettes for the four new guardians while
/// their detailed skinned assets are authored. Local forward is -Z, up is +Y.
/// A future GLB automatically takes precedence in GuardianRenderer.
/// </summary>
public partial class GuardianStarterModel : Node3D
{
    private readonly List<(Node3D Node, float Phase)> _legs = new();
    private Node3D? _head;
    private float _time;

    public static GuardianStarterModel? Create(GuardianDef def)
    {
        if (def.Id is not (GuardianCatalog.Fernback or GuardianCatalog.Dewhorn
            or GuardianCatalog.Hushbill or GuardianCatalog.Ruffleback)) return null;
        var model = new GuardianStarterModel { Name = "GuardianStarter_" + def.Archetype };
        var coat = Material("Fur", new Color((float)def.BaseColor.X, (float)def.BaseColor.Y, (float)def.BaseColor.Z));
        var accent = Material("Keratin", new Color((float)def.SecondaryColor.X, (float)def.SecondaryColor.Y, (float)def.SecondaryColor.Z));
        switch (def.Id)
        {
            case GuardianCatalog.Fernback: model.Fernback(coat, accent); break;
            case GuardianCatalog.Dewhorn: model.Dewhorn(coat, accent); break;
            case GuardianCatalog.Hushbill: model.Hushbill(coat, accent); break;
            case GuardianCatalog.Ruffleback: model.Ruffleback(coat, accent); break;
        }
        return model;
    }

    private static StandardMaterial3D Material(string name, Color color) => new()
    { ResourceName = name, AlbedoColor = color, Roughness = .86f };

    private static MeshInstance3D Oval(Node3D parent, string name, Vector3 at, Vector3 size, Material mat)
    {
        var node = new MeshInstance3D { Name = name, Position = at, Scale = size,
            Mesh = new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 24, Rings = 12, Material = mat } };
        parent.AddChild(node);
        return node;
    }

    private static void Link(Node3D parent, string name, Vector3 a, Vector3 b, float r1, float r2, Material mat)
    {
        var direction = b - a;
        var node = new MeshInstance3D { Name = name, Position = (a + b) * .5f,
            Quaternion = new Quaternion(Vector3.Up, direction.Normalized()),
            Mesh = new CylinderMesh { Height = direction.Length(), BottomRadius = r1,
                TopRadius = r2, RadialSegments = 12, Rings = 1, Material = mat } };
        parent.AddChild(node);
    }

    private static void Curve(Node3D parent, string name, Vector3[] points, float radius, Material mat)
    {
        for (int i = 1; i < points.Length; i++)
            Link(parent, name + i, points[i - 1], points[i], radius * (1 - .6f * (i - 1) / points.Length),
                radius * (1 - .6f * i / points.Length), mat);
    }

    private Node3D Head(Vector3 at)
    {
        _head = new Node3D { Name = "Head", Position = at };
        AddChild(_head);
        return _head;
    }

    private static void Eyes(Node3D head, float width, float height, float forward)
    {
        var eye = Material("Eye", new Color(.025f, .02f, .014f));
        eye.Roughness = .2f;
        for (int side = -1; side <= 1; side += 2)
            Oval(head, "Eye" + (side < 0 ? "L" : "R"), new Vector3(width * side, height, forward),
                new Vector3(.027f, .030f, .035f), eye);
    }

    private void Legs(float width, float front, float rear, float height, float radius, Material coat, bool hooves = false)
    {
        var foot = Material(hooves ? "HoofKeratin" : "PawSkin", new Color(.15f, .12f, .09f));
        for (int side = -1; side <= 1; side += 2)
        for (int row = 0; row < 2; row++)
        {
            var hip = new Node3D { Name = $"Leg_{side}_{row}", Position = new Vector3(side * width, height, row == 0 ? front : rear) };
            AddChild(hip);
            _legs.Add((hip, (side < 0 ? 0 : Mathf.Pi) + row * Mathf.Pi));
            var knee = new Vector3(0, -height * .46f, row == 0 ? .015f : -.035f);
            var ankle = new Vector3(0, -height + .045f, 0);
            Link(hip, "UpperLeg", Vector3.Zero, knee, radius * 1.3f, radius, coat);
            Oval(hip, "Knee", knee, Vector3.One * (radius * 2), coat);
            Link(hip, "LowerLeg", knee, ankle, radius * .8f, radius * .5f, coat);
            Oval(hip, "Foot", ankle + new Vector3(0, -.012f, -.025f),
                new Vector3(radius * 1.6f, .065f, hooves ? .095f : .13f), foot);
        }
    }

    private void Fernback(Material coat, Material stripes)
    {
        Oval(this, "LeanBody", new Vector3(0, .56f, .03f), new Vector3(.35f, .42f, .95f), coat);
        Oval(this, "Chest", new Vector3(0, .54f, -.28f), new Vector3(.36f, .47f, .35f), coat);
        Legs(.13f, -.29f, .36f, .48f, .052f, coat);
        Link(this, "Neck", new Vector3(0, .62f, -.30f), new Vector3(0, .77f, -.52f), .14f, .105f, coat);
        var head = Head(new Vector3(0, .77f, -.52f));
        Oval(head, "Skull", Vector3.Zero, new Vector3(.23f, .23f, .31f), coat);
        Oval(head, "LongMuzzle", new Vector3(0, -.04f, -.21f), new Vector3(.14f, .12f, .29f), coat);
        Oval(head, "Nose", new Vector3(0, -.03f, -.35f), new Vector3(.105f, .075f, .055f), stripes);
        var ear = Material("EarSkin", new Color(.53f, .29f, .24f));
        for (int side = -1; side <= 1; side += 2)
        {
            Oval(head, "RoundEar" + side, new Vector3(side * .105f, .13f, .035f), new Vector3(.095f, .15f, .048f), coat);
            Oval(head, "EarInterior" + side, new Vector3(side * .105f, .137f, .009f), new Vector3(.062f, .10f, .012f), ear);
        }
        Eyes(head, .106f, .015f, -.095f);
        Curve(this, "HeavyTail", new[] { new Vector3(0, .54f, .43f), new Vector3(0, .43f, .64f),
            new Vector3(0, .30f, .88f), new Vector3(0, .22f, 1.1f) }, .095f, coat);
        // Stripes follow the body ellipsoid instead of floating as flat decals.
        for (int stripe = 0; stripe < 6; stripe++)
        {
            float z = .02f + stripe * .063f;
            float section = MathF.Sqrt(1 - MathF.Pow((z - .03f) / .475f, 2));
            var points = new Vector3[13];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = -1.36f + i * 2.72f / 12;
                points[i] = new Vector3(.178f * section * MathF.Sin(angle), .56f + .213f * section * MathF.Cos(angle), z);
            }
            Curve(this, "FernStripe" + stripe, points, .012f, stripes);
            foreach (int side in new[] { -1, 1 })
            {
                var fork = points[side < 0 ? 2 : 10];
                Curve(this, "StripeFork" + stripe + side, new[] { fork,
                    fork + new Vector3(side * .012f, -.025f, -.028f), fork + new Vector3(side * .017f, -.045f, -.043f) }, .008f, stripes);
            }
        }
    }

    private void Dewhorn(Material coat, Material horn)
    {
        Oval(this, "Body", new Vector3(0, .64f, .04f), new Vector3(.43f, .51f, .87f), coat);
        Legs(.15f, -.27f, .30f, .55f, .035f, coat, true);
        Link(this, "Neck", new Vector3(0, .72f, -.28f), new Vector3(0, 1.0f, -.43f), .16f, .095f, coat);
        var head = Head(new Vector3(0, 1.0f, -.43f));
        Oval(head, "HeadShape", Vector3.Zero, new Vector3(.22f, .24f, .30f), coat);
        Oval(head, "SoftNose", new Vector3(0, -.09f, -.19f), new Vector3(.24f, .25f, .27f), coat);
        var dark = Material("NasalSkin", new Color(.20f, .15f, .11f));
        for (int side = -1; side <= 1; side += 2)
        {
            Oval(head, "Nostril" + side, new Vector3(side * .073f, -.15f, -.30f), new Vector3(.045f, .060f, .014f), dark);
            Oval(head, "Ear" + side, new Vector3(side * .17f, .06f, .04f), new Vector3(.21f, .08f, .075f), coat);
            Curve(head, "DewHorn" + side, new[] { new Vector3(side * .075f, .09f, .015f),
                new Vector3(side * .095f, .23f, .04f), new Vector3(side * .09f, .37f, .04f),
                new Vector3(side * .045f, .41f, .005f), new Vector3(side * .025f, .37f, -.015f) }, .03f, horn);
        }
        Eyes(head, .103f, .02f, -.05f);
        Link(this, "ShortTail", new Vector3(0, .76f, .42f), new Vector3(0, .62f, .57f), .035f, .018f, coat);
    }

    private void Hushbill(Material plumage, Material bill)
    {
        Oval(this, "Body", new Vector3(0, .77f, .06f), new Vector3(.46f, .65f, .48f), plumage);
        var dark = Material("LegSkin", new Color(.25f, .25f, .21f));
        for (int side = -1; side <= 1; side += 2)
        {
            var leg = new Node3D { Name = "WadingLeg" + side, Position = new Vector3(side * .10f, .58f, .035f) };
            AddChild(leg); _legs.Add((leg, side < 0 ? 0 : Mathf.Pi));
            Link(leg, "Shin", Vector3.Zero, new Vector3(0, -.54f, 0), .023f, .018f, dark);
            for (int toe = -1; toe <= 1; toe++)
                Link(leg, "Toe" + toe, new Vector3(0, -.55f, 0), new Vector3(toe * .07f, -.55f, -.14f), .013f, .004f, dark);
            Oval(this, "FoldedWing" + side, new Vector3(side * .19f, .79f, .09f), new Vector3(.12f, .53f, .43f), plumage);
        }
        Link(this, "Neck", new Vector3(0, .94f, -.04f), new Vector3(0, 1.27f, -.11f), .11f, .085f, plumage);
        var head = Head(new Vector3(0, 1.29f, -.12f));
        Oval(head, "HeadShape", Vector3.Zero, new Vector3(.31f, .29f, .30f), plumage);
        Oval(head, "GreatBill", new Vector3(0, -.04f, -.27f), new Vector3(.27f, .19f, .43f), bill);
        Link(head, "BillHook", new Vector3(0, -.04f, -.46f), new Vector3(0, -.13f, -.47f), .035f, .007f, bill);
        Eyes(head, .145f, .015f, -.075f);
        for (int side = -1; side <= 1; side += 2)
            Curve(head, "QuestionCrest" + side, new[] { new Vector3(side * .04f, .10f, .06f),
                new Vector3(side * .06f, .22f, .13f), new Vector3(side * .06f, .28f, .07f),
                new Vector3(side * .035f, .25f, .015f) }, .019f, plumage);
    }

    private void Ruffleback(Material shell, Material skin)
    {
        Oval(this, "BroadShell", new Vector3(0, .23f, .10f), new Vector3(.71f, .29f, .94f), shell);
        for (int row = -1; row <= 1; row++)
        for (int col = -1; col <= 1; col++)
            Oval(this, $"ShellScute{row}{col}", new Vector3(col * .19f, .325f - Math.Abs(col) * .025f, .1f + row * .23f),
                new Vector3(.22f, .12f, .28f), shell);
        Legs(.28f, -.16f, .35f, .15f, .055f, skin);
        Link(this, "Neck", new Vector3(0, .19f, -.29f), new Vector3(0, .19f, -.56f), .13f, .105f, skin);
        var head = Head(new Vector3(0, .19f, -.58f));
        Oval(head, "BroadHead", Vector3.Zero, new Vector3(.40f, .13f, .28f), skin);
        Link(head, "Snorkel", new Vector3(0, .02f, -.11f), new Vector3(0, .08f, -.28f), .037f, .015f, skin);
        Eyes(head, .16f, .052f, -.06f);
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 5; i++)
        {
            float z = -.20f + i * .085f;
            Curve(head, "Ribbon" + side + i, new[] { new Vector3(side * .14f, 0, z),
                new Vector3(side * .23f, -.035f, z + .02f), new Vector3(side * .27f, .015f, z + .04f),
                new Vector3(side * .23f, .04f, z + .055f) }, .015f, skin);
        }
        Link(this, "Tail", new Vector3(0, .20f, .51f), new Vector3(0, .10f, .70f), .037f, .005f, skin);
    }

    public void UpdatePose(double seconds, GuardianActivity activity)
    {
        _time += (float)seconds;
        bool moving = activity is GuardianActivity.Explore or GuardianActivity.Forage or GuardianActivity.Investigate
            or GuardianActivity.ReturnHome or GuardianActivity.Retreat;
        foreach (var (leg, phase) in _legs)
            leg.Rotation = new Vector3(moving ? MathF.Sin(_time * 4 + phase) * .16f : 0, 0, 0);
        if (_head != null)
            _head.Rotation = new Vector3(activity is GuardianActivity.Feed or GuardianActivity.Drink ? .20f : 0,
                moving ? 0 : MathF.Sin(_time * .6f) * .08f, 0);
    }
}
