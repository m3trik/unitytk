using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// How a <c>use</c> row's object sets its take off (<see cref="TaskSettings.use"/>): the sheet's <c>trigger</c> and
    /// <c>hold</c>, the take's speed and stretch (<see cref="ClipSettings"/>), and the kit's own.
    /// </summary>
    [Serializable]
    public sealed class UseSettings : ClipSettings
    {
        [Tooltip("What sets the take off. Select: a click (the usual). Hold: the object held pressed (with Hold 0 it plays while " +
                 "held: let go, it stops). Near: what the hand holds brought to it, or a click with it in hand, which leaves what " +
                 "the take moves in place. Set it for one object in the Manifest window: the chip's Settings for that object.")]
        public UseTrigger trigger = UseTrigger.Select;

        [Tooltip("Seconds a press is held before the take plays (a timed press, with Select or Hold); 0: none (with Hold, it " +
                 "plays while held).")]
        [Min(0f)]
        public float hold;

        [Tooltip("A take played while held plays back to its start as it is let go, rather than stopping where it stands: the " +
                 "user holds until it is done.")]
        public bool rewind;

        [Tooltip("What the object, selected without what it needs in hand, says (the page stays): {0} is that, its name made " +
                 "readable (KEY_GRP: key); empty says nothing.")]
        public string refused = "Take the {0} first.";

        [Tooltip("With Near: what the take moves into place stays where it puts it, out of the hand: fitted. Off: it comes back " +
                 "to the hand as its take ends, as a tool does (a scanner held up to a tag).")]
        public bool stays = true;
    }
}
