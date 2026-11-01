using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>The numbers are saved with the order: new states get new numbers.</summary>
    public enum PutBackState { Standing = 0, Down = 1, Back = 2, Skipped = 3 }

    /// <summary>
    /// One of the player's pieces an upgrade takes down and puts back where it was (1.6, the own-pieces design's point 4):
    /// saved with the order, so a reload or another player's computer taking over mid-upgrade still puts it back.
    /// </summary>
    public sealed class PutBackRecord
    {
        public PutBackRecord(string prefab, Vec3 position, Quat rotation, long creator, string name)
        {
            Prefab = prefab ?? "";
            Position = position;
            Rotation = rotation;
            Creator = creator;
            Name = name ?? "";
        }

        public string Prefab { get; }
        public Vec3 Position { get; }
        public Quat Rotation { get; }
        /// <summary>The player who placed it: it goes back as theirs.</summary>
        public long Creator { get; }
        /// <summary>The piece's own name token (<c>$piece_…</c>), for the log and the window.</summary>
        public string Name { get; }
        public PutBackState State = PutBackState.Standing;
        public int Failures;
        /// <summary>
        /// What it held, saved as it came down and set on it as it goes back (the review's finding 1: a sign's text, a
        /// portal's tag, a torch's fuel); the game's side writes and reads it (<c>PiecePlacer.KeptValues</c>). "" for none.
        /// </summary>
        public string Kept = "";

        /// <summary>Still to come down, or down and still to go back.</summary>
        public bool Open => State == PutBackState.Standing || State == PutBackState.Down;

        /// <summary>It fell or couldn't be placed. True when it has just failed often enough to be left out (skipped).</summary>
        public bool MarkFailed()
        {
            Failures++;
            if (Failures < BuildOrderRecord.MaxFailures) return false;
            State = PutBackState.Skipped;
            return true;
        }

        internal string Encode() => RecordCodec.Encode(new[]
        {
            new KeyValuePair<string, string>("p", Prefab),
            new KeyValuePair<string, string>("at", RecordCodec.FormatFloats(new[] { Position.X, Position.Y, Position.Z })),
            new KeyValuePair<string, string>("rot", RecordCodec.FormatFloats(new[] { Rotation.X, Rotation.Y, Rotation.Z, Rotation.W })),
            new KeyValuePair<string, string>("by", Creator.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("n", Name),
            new KeyValuePair<string, string>("s", RecordCodec.FormatInt((int)State)),
            new KeyValuePair<string, string>("f", RecordCodec.FormatInt(Failures)),
        }.Concat(Kept.Length == 0 ? Enumerable.Empty<KeyValuePair<string, string>>() : new[] { new KeyValuePair<string, string>("k", Kept) }));

        /// <summary>Null for a damaged entry (no kind, a place or turn that isn't finite numbers). Never throws.</summary>
        internal static PutBackRecord Decode(string text)
        {
            Dictionary<string, string> f = RecordCodec.Decode(text ?? "");
            string prefab = RecordCodec.Get(f, "p");
            float[] at = RecordCodec.ParseFloats(RecordCodec.Get(f, "at"), 3);
            float[] rot = RecordCodec.ParseFloats(RecordCodec.Get(f, "rot"), 4);
            if (prefab.Length == 0 || at == null || rot == null) return null;
            long.TryParse(RecordCodec.Get(f, "by"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long creator);
            int state = RecordCodec.ParseInt(RecordCodec.Get(f, "s"), 0);
            return new PutBackRecord(prefab, new Vec3(at[0], at[1], at[2]), new Quat(rot[0], rot[1], rot[2], rot[3]), creator, RecordCodec.Get(f, "n"))
            {
                State = System.Enum.IsDefined(typeof(PutBackState), state) ? (PutBackState)state : PutBackState.Standing,
                Failures = System.Math.Max(0, RecordCodec.ParseInt(RecordCodec.Get(f, "f"), 0)),
                Kept = RecordCodec.Get(f, "k"),
            };
        }
    }
}
