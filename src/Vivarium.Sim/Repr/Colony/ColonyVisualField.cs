using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// <summary>Four nonnegative visual coverage channels computed from authoritative colony state.</summary>
public readonly record struct ColonyVisualSample(double Moss, double Crust, double Foliose, double Fruticose, double Health, double Thickness, double Moisture);
public static class ColonyVisualField
{
    public static ColonyVisualSample Sample(ColonySystem system, Vec2 point)
    {
        double moss=0,crust=0,foliose=0,fruticose=0,health=0,thickness=0,total=0;
        foreach (var occ in system.Sample(point).Occupants)
        {
            ColonySpecies? species=null;
            foreach(var candidate in system.Species) if(candidate.Id==occ.SpeciesId) { species=candidate; break; }
            if(species==null || species.Kind==ColonyKind.Litter) continue;
            double value=occ.Coverage;
            total+=value; health+=value*occ.Health;
            thickness+=value*Math.Min(1,occ.Thickness/Math.Max(1e-6,species.MaxThickness));
            switch(species.Kind) { case ColonyKind.Moss: moss+=value; break; case ColonyKind.CrustoseLichen: crust+=value; break; case ColonyKind.FolioseLichen: foliose+=value; break; case ColonyKind.FruticoseLichen: fruticose+=value; break; }
        }
        return new(Math.Clamp(moss,0,1),Math.Clamp(crust,0,1),Math.Clamp(foliose,0,1),Math.Clamp(fruticose,0,1),
            total>1e-9 ? Math.Clamp(health/total,0,1) : 0,
            total>1e-9 ? Math.Clamp(thickness/total,0,1) : 0, Math.Clamp(system.MoistureAt(point),0,1));
    }
}
