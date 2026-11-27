using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Placement.Tests
{
    /// <summary>
    /// The room the Placement tests stand in: a floor 20 m square and a part (a 0.2 m cube, a metre up), each
    /// test adding what it puts around it (<see cref="Box"/>); all taken away after.
    /// </summary>
    public abstract class RoomFixture
    {
        /// <summary>The room's root: everything a test makes goes under it.</summary>
        protected GameObject Room;

        /// <summary>The part the tests place things by.</summary>
        protected Transform Part;

        /// <summary>The part's renderer's bounds.</summary>
        protected Bounds PartBounds => Part.GetComponent<Renderer>().bounds;

        [SetUp]
        public void MakeRoom()
        {
            Room = new GameObject("room");
            Box("FLOOR", new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20));
            Part = Box("PART", new Vector3(0, 1f, 0), Vector3.one * 0.2f);
        }

        [TearDown]
        public void TakeRoomAway()
        {
            Object.DestroyImmediate(Room);
            Physics.SyncTransforms();
        }

        /// <summary>A cube named <paramref name="name"/> under the room, at <paramref name="at"/>, <paramref name="size"/> across.</summary>
        protected Transform Box(string name, Vector3 at, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(Room.transform);
            box.transform.position = at;
            box.transform.localScale = size;
            return box.transform;
        }
    }
}
