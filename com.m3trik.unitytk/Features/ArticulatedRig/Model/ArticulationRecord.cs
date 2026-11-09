// ----------------------------------------------------------------------------
// The articulation record's reader policy, the hand-written half of the partial
// class whose declared shape m3trik/scripts/sync_scene_records.py generates
// beside it (ArticulationRecord.g.cs, from pythontk's SceneRecords). In the
// engine-free model part: the model, the runtime component (a player reads the
// record it holds) and the importer all read the record through it.
// ----------------------------------------------------------------------------
using System.Collections.Generic;

/// <summary>
/// Reading the <c>articulation</c> record: the reader's policy -- its channel,
/// the versions it reads -- here; the record's declared shape
/// (<see cref="Shape"/>: its payload, rigs, joints, channels and grabs) in
/// ArticulationRecord.g.cs, generated from pythontk's declaration. A record is
/// read the one way every record is: <c>UnitytkRecords.Read(Channel, json,
/// CurrentVersion, Shape.Payload.Read, where, out refusal)</c>.
/// </summary>
public static partial class ArticulationRecord
{
    /// <summary>The channel name on the <c>data_export</c> carrier
    /// (<see cref="UnitytkRecords.Articulation"/>, its one declaration).</summary>
    public const string Channel = UnitytkRecords.Articulation;

    /// <summary>The payload's schema version (<see cref="UnitytkRecords.VersionOf"/>:
    /// 0 when it states none, -1 when it is not a whole number).</summary>
    public static int Version(IDictionary<string, object> payload) => UnitytkRecords.VersionOf(payload);
}
