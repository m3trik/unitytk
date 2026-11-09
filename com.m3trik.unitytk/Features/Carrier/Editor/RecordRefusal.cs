namespace M3trik.Unitytk.Carrier
{
    /// <summary>One record a model carries that this unitytk refuses to read.</summary>
    public sealed class RecordRefusal
    {
        /// <summary>The model's asset path.</summary>
        public string assetPath;

        /// <summary>The record's key.</summary>
        public string key;

        /// <summary>The version the record states (0: none; -1: not a whole number or not JSON).</summary>
        public int version;

        /// <summary>The version this unitytk reads.</summary>
        public int supported;

        /// <summary>Why it is refused, and what to do.</summary>
        public string message;
    }
}
