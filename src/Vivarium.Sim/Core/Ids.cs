namespace Vivarium.Sim.Core;

public enum EntityKind : byte { None = 0, Rock = 1, Log = 2, Gravel = 3, Spring = 4, Flora = 5, Fauna = 6, Genome = 7 }

/// <summary>
/// Persistent identifier. The high byte is the kind (for readable diagnostics), and the low 56 bits
/// are a world-wide monotonic serial. Ids are never reused, and the allocator state is saved.
/// </summary>
public readonly record struct EntityId(ulong Value) : IComparable<EntityId>
{
    public static readonly EntityId None = new(0);
    public EntityKind Kind => (EntityKind)(Value >> 56);
    public ulong Serial => Value & 0x00FF_FFFF_FFFF_FFFFUL;
    public bool IsNone => Value == 0;
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsNone ? "none" : $"{Kind.ToString().ToLowerInvariant()}-{Serial}";
    public static EntityId Make(EntityKind kind, ulong serial) => new(((ulong)kind << 56) | (serial & 0x00FF_FFFF_FFFF_FFFFUL));
}

public sealed class IdAllocator
{
    /// <summary>Last issued serial (persisted).</summary>
    public ulong LastSerial { get; set; }

    public EntityId Next(EntityKind kind)
    {
        if (kind == EntityKind.None) throw new ArgumentException("Cannot allocate an id of kind None.");
        LastSerial++;
        return EntityId.Make(kind, LastSerial);
    }

    /// <summary>Ensures future ids cannot collide with an externally restored id.</summary>
    public void Observe(EntityId id) { if (id.Serial > LastSerial) LastSerial = id.Serial; }
}
