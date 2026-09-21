using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    /// <summary>One item from a scan: the game's text for it and where it sits, relative to the stash area.</summary>
    public class SavedItem
    {
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public int Count { get; set; }          // count read from the screen (0 = the text's stack size applies)
        public bool CountUnread { get; set; }   // count needed reading from the screen but couldn't be read
    }

    /// <summary>The last scan of a tab. Prices are applied when shown, so they follow poe.ninja updates.</summary>
    public class TabResult
    {
        public string Key { get; set; }
        public DateTime ScannedAt { get; set; }
        public List<SavedItem> Items { get; set; }

        public TabResult() { Items = new List<SavedItem>(); }
    }

    public class PricedItem
    {
        public ParsedItem Item;
        public PriceInfo Price;
        public int Qty;
        public bool CountUnread;
        public double TotalDiv;
        public Rectangle Bounds;   // screen coordinates
    }

    public static class ResultStore
    {
        static string FilePath { get { return Path.Combine(AppSettings.Dir, "results.json"); } }

        public static Dictionary<string, TabResult> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    Dictionary<string, TabResult> d = js.Deserialize<Dictionary<string, TabResult>>(File.ReadAllText(FilePath));
                    if (d != null)
                    {
                        // The serializer hands dates back in UTC.
                        foreach (TabResult tr in d.Values) tr.ScannedAt = tr.ScannedAt.ToLocalTime();
                        return d;
                    }
                }
            }
            catch { }
            return new Dictionary<string, TabResult>();
        }

        public static void DeleteAll()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }

        public static void Save(Dictionary<string, TabResult> results)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                File.WriteAllText(FilePath, js.Serialize(results));
            }
            catch { }
        }

        public static TabResult FromScan(string key, ScanResult res, Rectangle region)
        {
            TabResult tr = new TabResult { Key = key, ScannedAt = DateTime.Now };
            double w = Math.Max(1, region.Width), h = Math.Max(1, region.Height);
            foreach (ScanItem si in res.Items)
                tr.Items.Add(new SavedItem
                {
                    Text = si.Text,
                    X = (si.Bounds.X - region.X) / w,
                    Y = (si.Bounds.Y - region.Y) / h,
                    W = si.Bounds.Width / w,
                    H = si.Bounds.Height / h,
                    Count = si.Item != null && si.Item.NeedsCount && !si.CountUnread ? si.Qty : 0,
                    CountUnread = si.CountUnread
                });
            return tr;
        }

        /// <summary>Items with current prices; <paramref name="region"/> places them on screen (may be empty).</summary>
        public static List<PricedItem> Price(TabResult tr, PriceTable table, Rectangle region)
        {
            List<PricedItem> list = new List<PricedItem>();
            if (tr == null) return list;
            foreach (SavedItem s in tr.Items)
            {
                ParsedItem it = ItemParser.Parse(s.Text);
                if (it == null) continue;
                PricedItem p = new PricedItem { Item = it, Qty = s.Count > 0 ? s.Count : it.Stack, CountUnread = s.CountUnread };
                p.Price = table != null ? table.Lookup(it) : null;
                p.TotalDiv = p.Price != null ? p.Price.Div * p.Qty : 0;
                p.Bounds = new Rectangle(region.X + (int)Math.Round(s.X * region.Width), region.Y + (int)Math.Round(s.Y * region.Height),
                                         (int)Math.Round(s.W * region.Width), (int)Math.Round(s.H * region.Height));
                list.Add(p);
            }
            return list;
        }

        public static double Total(TabResult tr, PriceTable table)
        {
            double sum = 0;
            foreach (PricedItem p in Price(tr, table, Rectangle.Empty)) sum += p.TotalDiv;
            return sum;
        }
    }
}
