using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    public class PriceInfo
    {
        public string Name;
        public string Category;
        public double Div;          // value of one unit in Divine Orbs
        public int Listings;
    }

    /// <summary>Immutable snapshot of poe.ninja prices for one league.</summary>
    public class PriceTable
    {
        public string League;
        public DateTime LoadedAt;
        public double ExPerDiv;
        public double ChaosPerDiv;
        public List<string> Failed = new List<string>();

        internal readonly Dictionary<string, PriceInfo> ByName = new Dictionary<string, PriceInfo>();
        internal readonly Dictionary<string, PriceInfo> UniqueByNameBase = new Dictionary<string, PriceInfo>();
        internal readonly Dictionary<string, PriceInfo> UniqueByName = new Dictionary<string, PriceInfo>();

        public int Count { get { return ByName.Count + UniqueByNameBase.Count; } }

        static string Key(string s) { return s.Trim().ToLowerInvariant(); }

        public PriceInfo Lookup(ParsedItem it)
        {
            if (it == null || it.Name == null) return null;
            string rarity = (it.Rarity ?? "").ToLowerInvariant();
            PriceInfo p;

            if (rarity == "unique")
            {
                if (it.Unidentified || it.BaseType == null) return null;
                if (UniqueByNameBase.TryGetValue(Key(it.Name + "|" + it.BaseType), out p)) return p;
                if (UniqueByName.TryGetValue(Key(it.Name), out p)) return p;
                return null;
            }
            if (rarity == "rare" || rarity == "magic") return null;

            string n = it.Name;
            if (n.StartsWith("Superior ")) n = n.Substring(9);
            if (it.Level > 0 && ByName.TryGetValue(Key(n + " (Level " + it.Level + ")"), out p)) return p;
            if (ByName.TryGetValue(Key(n), out p)) return p;
            return null;
        }

        internal void AddExchange(string name, string category, double div)
        {
            string k = Key(name);
            if (!ByName.ContainsKey(k))
                ByName[k] = new PriceInfo { Name = name, Category = category, Div = div };
        }

        internal void AddUnique(string name, string baseType, string category, double div, int listings)
        {
            PriceInfo info = new PriceInfo { Name = name + " (" + baseType + ")", Category = category, Div = div, Listings = listings };
            Keep(UniqueByNameBase, Key(name + "|" + baseType), info);
            Keep(UniqueByName, Key(name), info);
        }

        // Variants (corrupted, different rolls...) share names; keep the most-listed one as the "typical" price.
        static void Keep(Dictionary<string, PriceInfo> d, string k, PriceInfo info)
        {
            PriceInfo old;
            if (!d.TryGetValue(k, out old) || info.Listings > old.Listings) d[k] = info;
        }
    }

    public static class PriceService
    {
        const string Base = "https://poe.ninja/poe2/api/economy/";
        const string UserAgent = "PoeStashPricer/1.0 (desktop stash pricing tool)";

        static readonly string[] ExchangeTypes =
        {
            "Currency", "Fragments", "Abyss", "UncutGems", "LineageSupportGems", "Essences", "SoulCores",
            "Idols", "Runes", "Ritual", "Expedition", "Delirium", "Breach", "Verisium"
        };

        static readonly string[] StashTypes =
        {
            "UniqueWeapons", "UniqueArmours", "UniqueAccessories", "UniqueFlasks", "UniqueCharms",
            "UniqueJewels", "UniqueSanctumRelics", "UniqueTablets", "PrecursorTablets"
        };

        static JavaScriptSerializer Json()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        static string Get(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Accept = "application/json";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 20000;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        static string Q(string s) { return Uri.EscapeDataString(s); }

        static double Num(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) && o != null ? o.ToString() : null;
        }

        static IEnumerable<Dictionary<string, object>> List(Dictionary<string, object> d, string k)
        {
            object o;
            if (!d.TryGetValue(k, out o) || !(o is IEnumerable)) yield break;
            foreach (object x in (IEnumerable)o)
            {
                Dictionary<string, object> dx = x as Dictionary<string, object>;
                if (dx != null) yield return dx;
            }
        }

        public static List<string> GetLeagues()
        {
            List<string> res = new List<string>();
            object parsed = Json().DeserializeObject(Get(Base + "leagues"));
            foreach (object o in (IEnumerable)parsed)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                string id = Str(d, "id") ?? Str(d, "name");
                if (id != null) res.Add(id);
            }
            return res;
        }

        public static PriceTable Load(string league, Action<string> progress)
        {
            PriceTable t = new PriceTable();
            t.League = league;
            int step = 0, total = ExchangeTypes.Length + StashTypes.Length;

            foreach (string type in ExchangeTypes)
            {
                step++;
                if (progress != null) progress("Fiyatlar yükleniyor (" + step + "/" + total + "): " + type);
                try
                {
                    Dictionary<string, object> root = (Dictionary<string, object>)Json().DeserializeObject(
                        Get(Base + "exchange/current/overview?league=" + Q(league) + "&type=" + type));
                    ReadRates(t, root);
                    Dictionary<string, string> names = new Dictionary<string, string>();
                    foreach (Dictionary<string, object> item in List(root, "items"))
                    {
                        string id = Str(item, "id"), name = Str(item, "name");
                        if (id != null && name != null) names[id] = name;
                    }
                    foreach (Dictionary<string, object> line in List(root, "lines"))
                    {
                        string id = Str(line, "id"), name;
                        object pv;
                        if (id == null || !names.TryGetValue(id, out name) || !line.TryGetValue("primaryValue", out pv)) continue;
                        t.AddExchange(name, type, Num(pv));
                    }
                }
                catch (Exception) { t.Failed.Add(type); }
            }

            foreach (string type in StashTypes)
            {
                step++;
                if (progress != null) progress("Fiyatlar yükleniyor (" + step + "/" + total + "): " + type);
                try
                {
                    Dictionary<string, object> root = (Dictionary<string, object>)Json().DeserializeObject(
                        Get(Base + "stash/current/item/overview?league=" + Q(league) + "&type=" + type));
                    ReadRates(t, root);
                    foreach (Dictionary<string, object> line in List(root, "lines"))
                    {
                        string name = Str(line, "name"), baseType = Str(line, "baseType");
                        object pv, lc;
                        if (name == null || !line.TryGetValue("primaryValue", out pv)) continue;
                        line.TryGetValue("listingCount", out lc);
                        t.AddUnique(name, baseType ?? "", type, Num(pv), (int)Num(lc));
                    }
                }
                catch (Exception) { t.Failed.Add(type); }
            }

            t.LoadedAt = DateTime.Now;
            return t;
        }

        static void ReadRates(PriceTable t, Dictionary<string, object> root)
        {
            if (t.ExPerDiv > 0) return;
            object core;
            if (!root.TryGetValue("core", out core)) return;
            Dictionary<string, object> c = core as Dictionary<string, object>;
            object rates;
            if (c == null || !c.TryGetValue("rates", out rates)) return;
            Dictionary<string, object> r = rates as Dictionary<string, object>;
            if (r == null) return;
            object ex, ch;
            if (r.TryGetValue("exalted", out ex)) t.ExPerDiv = Num(ex);
            if (r.TryGetValue("chaos", out ch)) t.ChaosPerDiv = Num(ch);
        }
    }
}
