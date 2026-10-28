using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The <c>{ ok, reason?, … }</c> a rule returns. A failed rule (not enough RAM, a locked perk, too heavy) is a result, not an exception; a programmer's error (an
    /// unknown perk id, a corrupt save) throws. Only the fields a rule sets are set, and <see cref="ToJson"/> writes only those, as the prototype's objects have them.
    /// </summary>
    public sealed class RpgResult
    {
        public bool Ok;
        public string Reason;
        public List<string> Missing;
        public int? Cost, Rank, Value, Remaining, Need, Free, Removed, Price, Qty, Money, Delta, Index, Refunded;
        public string Slot, VendorId;
        public Perk Perk;
        public CyberwareDef Ware;
        public Item Item;
        public bool? Merged;
        /// <summary>Whether <see cref="Replaced"/> was given (it may be given as null).</summary>
        public bool HasReplaced;
        public Item Replaced;
        public JsonObject Vendor;
        /// <summary>Anything a later rule adds that has no field above.</summary>
        public JsonObject Extra;

        public static RpgResult Fail(string reason) => new RpgResult { Ok = false, Reason = reason };

        public static RpgResult Success() => new RpgResult { Ok = true };

        /// <summary>The result as the prototype's object would serialise; an item is written whole (the tests reduce it to its uid).</summary>
        public JsonObject ToJson()
        {
            var o = new JsonObject { ["ok"] = Ok };
            if (Reason != null) o["reason"] = Reason;
            if (Missing != null) o["missing"] = new List<object>(Missing);
            Put(o, "cost", Cost);
            Put(o, "rank", Rank);
            Put(o, "value", Value);
            Put(o, "remaining", Remaining);
            Put(o, "need", Need);
            Put(o, "free", Free);
            Put(o, "removed", Removed);
            Put(o, "price", Price);
            Put(o, "qty", Qty);
            Put(o, "money", Money);
            Put(o, "delta", Delta);
            Put(o, "index", Index);
            Put(o, "refunded", Refunded);
            if (Slot != null) o["slot"] = Slot;
            if (VendorId != null) o["vendorId"] = VendorId;
            if (Perk != null) o["perk"] = Perk;
            if (Ware != null) o["ware"] = Ware;
            if (Item != null) o["item"] = Item;
            if (Merged.HasValue) o["merged"] = Merged.Value;
            if (HasReplaced) o["replaced"] = Replaced;
            if (Vendor != null) o["vendor"] = Vendor;
            if (Extra != null)
            {
                foreach (KeyValuePair<string, object> kv in Extra) o[kv.Key] = kv.Value;
            }
            return o;
        }

        static void Put(JsonObject o, string key, int? v)
        {
            if (v.HasValue) o[key] = (double)v.Value;
        }
    }
}
