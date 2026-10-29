using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;

namespace GexProfile
{
    /// <summary>
    /// Gamma exposure per strike drawn as a horizontal histogram on the price chart,
    /// with call wall, put wall and max pain levels. Option chain comes from the chart symbol's connection.
    /// </summary>
    public class GexProfileIndicator : Indicator
    {
        private const int SourceOpenInterest = 0, SourceDayVolume = 1;
        private const int HistogramNet = 0, HistogramSplit = 1;
        private const int PositionRight = 0, PositionLeft = 1;

        // Options that have expired (or are about to) still get a little time value so gamma stays finite.
        private const double MinDaysToExpiry = 15.0 / 1440.0;
        private const int LabelsPerSide = 3;

        // In Auto expiration mode, an expiry whose OI is still all zero this long after loading is skipped (new listings have no OI until CME publishes it).
        private static readonly TimeSpan EmptyExpiryGrace = TimeSpan.FromSeconds(20);
        private const int MaxEmptySkips = 3;

        #region Settings

        [InputParameter("Source", 10, variants: new object[] { "Open Interest", SourceOpenInterest, "Day Volume (live positioning)", SourceDayVolume })]
        public int Source = SourceOpenInterest;

        [InputParameter("Histogram", 20, variants: new object[] { "Net (one bar per strike)", HistogramNet, "Split (calls right, puts left)", HistogramSplit })]
        public int Histogram = HistogramNet;

        [InputParameter("Walls per side", 30, 1, 10, 1, 0)]
        public int WallsPerSide = 1;

        [InputParameter("Wall labels", 40)]
        public bool WallLabels = false;

        [InputParameter("Position", 50, variants: new object[] { "Right", PositionRight, "Left", PositionLeft })]
        public int Position = PositionRight;

        [InputParameter("Profile width (%)", 60, 5, 80, 1, 0)]
        public int ProfileWidth = 30;

        [InputParameter("Expiration", 70, variants: new object[] { "Auto (nearest)", 0, "2nd nearest", 1, "3rd nearest", 2, "4th nearest", 3 })]
        public int Expiration = 0;

        [InputParameter("Aggregate expirations", 80, 1, 10, 1, 0)]
        public int Aggregate = 1;

        [InputParameter("Strikes per side", 90, 1, 500, 1, 0)]
        public int StrikesPerSide = 50;

        [InputParameter("Wide range (all strikes)", 100)]
        public bool WideRange = false;

        private static readonly Dictionary<string, string> SettingGroups = new()
        {
            ["Source"] = "GEX",
            ["Histogram"] = "GEX",
            ["Walls per side"] = "GEX",
            ["Wall labels"] = "GEX",
            ["Position"] = "Profile",
            ["Profile width (%)"] = "Profile",
            ["Expiration"] = "Options",
            ["Aggregate expirations"] = "Options",
            ["Strikes per side"] = "Options",
            ["Wide range (all strikes)"] = "Options",
        };

        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;
                var groups = new Dictionary<string, SettingItemSeparatorGroup>
                {
                    ["GEX"] = new SettingItemSeparatorGroup("GEX", 0),
                    ["Profile"] = new SettingItemSeparatorGroup("Profile", 1),
                    ["Options"] = new SettingItemSeparatorGroup("Options", 2),
                };
                foreach (var item in settings)
                    if (SettingGroups.TryGetValue(item.Name, out var group))
                        item.SeparatorGroup = groups[group];
                return settings;
            }
            set => base.Settings = value;
        }

        #endregion

        private static readonly Color CallColor = Color.FromArgb(0, 209, 160);
        private static readonly Color PutColor = Color.FromArgb(150, 80, 230);
        private static readonly Color MaxPainColor = Color.FromArgb(230, 72, 72);
        private static readonly Color TextColor = Color.FromArgb(220, 224, 230);
        private static readonly Color AxisColor = Color.FromArgb(90, 128, 128, 140);
        private static readonly Font LabelFont = new("Segoe UI", 8f);
        private static readonly Font HeaderFont = new("Segoe UI", 9f);
        private static readonly TimeZoneInfo Eastern = FindEastern();

        private sealed class Leg
        {
            public Symbol Symbol;
            public bool IsCall;
            public double Strike;
            public DateTime ExpiryUtc;
        }

        private sealed class Snapshot
        {
            public List<StrikeRow> Rows;
            public double StrikeStep;
            public double MaxPain;
            public List<(double strike, double value)> CallWalls, PutWalls;
            public string Header;
            public bool HasData;
            public DateTime FirstExpiryUtc;
        }

        private readonly List<Leg> legs = new();
        private CancellationTokenSource cts;
        private Timer timer;
        private int computing;
        private Symbol underlying;
        private volatile Snapshot snapshot;
        private volatile string status;
        private bool sampleLogged;
        private DateTime loadedAt;
        private bool active;
        private int emptySkips;
        private volatile bool hasLaterExpiry;

        public GexProfileIndicator()
        {
            Name = "GEX Profile";
            Description = "Gamma exposure per strike with call wall, put wall and max pain";
            SeparateWindow = false;
            OnBackGround = true;
        }

        public override string ShortName => "GEX";

        protected override void OnInit()
        {
            lock (legs)
            {
                active = true;
                emptySkips = 0;
                StartLoad();
            }
        }

        protected override void OnClear()
        {
            lock (legs)
            {
                active = false;
                Teardown();
            }
        }

        private void StartLoad()
        {
            status = "Loading option chain…";
            sampleLogged = false;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            Task.Run(() => LoadChain(token), token);
        }

        // Drops the current chain and loads it again, e.g. to move past an empty or expired expiry.
        private void Reload()
        {
            lock (legs)
            {
                if (!active)
                    return;
                Teardown();
                StartLoad();
            }
            CurrentChart?.RedrawBuffer();
        }

        private void Teardown()
        {
            cts?.Cancel();
            lock (legs)
            {
                timer?.Dispose();
                timer = null;
                foreach (var leg in legs)
                    Unsubscribe(leg.Symbol);
                legs.Clear();
                if (underlying != null && underlying != Symbol)
                    Unsubscribe(underlying);
                underlying = null;
            }
            snapshot = null;
        }

        #region Option chain

        private void LoadChain(CancellationToken token)
        {
            try
            {
                var chartSymbol = Symbol;
                if (chartSymbol == null)
                {
                    status = "No chart symbol";
                    return;
                }

                Log($"chart symbol {chartSymbol.Name} id={chartSymbol.Id} type={chartSymbol.SymbolType} options={chartSymbol.AvailableOptions} underlier={chartSymbol.Underlier?.Name}");

                var series = FindSeries(chartSymbol);
                foreach (var s in series)
                    Log($"series {s.Name} id={s.Id} exp={s.ExpirationDate:yyyy-MM-dd HH:mm} ({s.ExpirationDate.Kind}) type={s.SerieType} underlier={s.UnderlierId}");
                if (series.Count == 0)
                {
                    status = $"No option series found for {chartSymbol.Name}";
                    return;
                }

                var nowUtc = DateTime.UtcNow;
                var upcoming = series
                    .GroupBy(s => s.ExpirationDate.Date)
                    .Where(g => ExpiryUtc(g.First().ExpirationDate) > nowUtc)
                    .OrderBy(g => g.Key)
                    .ToList();
                int skip = Expiration + emptySkips;
                var expiries = upcoming.Skip(skip).Take(Math.Max(1, Aggregate)).ToList();
                hasLaterExpiry = upcoming.Count > skip + expiries.Count;
                if (expiries.Count == 0)
                {
                    status = "No upcoming expirations";
                    return;
                }

                // Several series can share a date; use the one with the biggest chain so nothing is counted twice.
                var chains = new List<(OptionSerie serie, List<Symbol> options)>();
                foreach (var day in expiries)
                {
                    var best = day
                        .Select(s => (serie: s, options: (Core.Instance.GetStrikes(s) ?? new List<Symbol>()).Where(o => o != null).ToList()))
                        .OrderByDescending(c => c.options.Count)
                        .First();
                    Log($"using {best.serie.Name} exp={best.serie.ExpirationDate:yyyy-MM-dd} with {best.options.Count} option symbols");
                    if (best.options.Count > 0)
                        chains.Add(best);
                    token.ThrowIfCancellationRequested();
                }
                if (chains.Count == 0)
                {
                    status = "Option chain is empty";
                    return;
                }

                var under = chains[0].options[0].Underlier ?? chartSymbol;
                if (under.Id != chartSymbol.Id)
                {
                    Log($"options underlier {under.Name} differs from chart symbol, subscribing to it for price");
                    Subscribe(under);
                }

                double f = double.NaN;
                for (int i = 0; i < 60 && !(f > 0); i++)
                {
                    f = PriceOf(under);
                    if (!(f > 0))
                        token.WaitHandle.WaitOne(250);
                    token.ThrowIfCancellationRequested();
                }
                if (!(f > 0))
                {
                    status = $"No price for {under.Name}";
                    return;
                }

                var newLegs = new List<Leg>();
                foreach (var (serie, options) in chains)
                {
                    var strikes = options.Select(o => o.StrikePrice).Distinct().OrderBy(k => k).ToList();
                    var keep = new HashSet<double>(strikes);
                    if (!WideRange)
                    {
                        int atm = strikes.IndexOf(strikes.OrderBy(k => Math.Abs(k - f)).First());
                        keep = new HashSet<double>(strikes.Skip(Math.Max(0, atm - StrikesPerSide)).Take(2 * StrikesPerSide + 1));
                    }
                    foreach (var o in options.Where(o => keep.Contains(o.StrikePrice)))
                    {
                        var exp = o.ExpirationDate != default ? o.ExpirationDate : serie.ExpirationDate;
                        newLegs.Add(new Leg { Symbol = o, IsCall = o.OptionType == OptionType.Call, Strike = o.StrikePrice, ExpiryUtc = ExpiryUtc(exp) });
                    }
                    Log($"{serie.Name}: {keep.Count} of {strikes.Count} strikes around {f}, expiry {ExpiryUtc(serie.ExpirationDate):yyyy-MM-dd HH:mm} UTC");
                }

                lock (legs)
                {
                    if (token.IsCancellationRequested)
                    {
                        if (under != chartSymbol)
                            Unsubscribe(under);
                        return;
                    }
                    underlying = under;
                    legs.AddRange(newLegs);
                    foreach (var leg in legs)
                        Subscribe(leg.Symbol);
                    loadedAt = DateTime.UtcNow;
                    timer = new Timer(_ => Tick(), null, 500, 1000);
                }
                status = "Waiting for option data…";
                Log($"subscribed to {newLegs.Count} options, multiplier {Multiplier(under, f)}");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                status = "Error: " + ex.Message;
                Core.Instance.Loggers.Log(ex, "[GEX Profile] loading option chain failed", LoggingLevel.Error, Symbol?.Connection?.Name);
            }
        }

        private static List<OptionSerie> FindSeries(Symbol chartSymbol)
        {
            foreach (var candidate in new[] { chartSymbol, chartSymbol.Underlier })
            {
                if (candidate == null)
                    continue;
                // CME event contracts (ECG…) come back as option series too; they are binaries, not gamma-bearing options.
                var list = Core.Instance.GetOptionSeries(candidate)?.Where(s => s != null && s.SerieType != OptionSerieType.BinaryContracts).ToList();
                if (list == null || list.Count == 0)
                    continue;
                // Asked via the futures root we may get options on every contract month; prefer the chart's contract.
                var own = list.Where(s => s.UnderlierId == chartSymbol.Id).ToList();
                return own.Count > 0 ? own : list;
            }
            return new List<OptionSerie>();
        }

        private void Subscribe(Symbol s)
        {
            s.NewQuote += OnQuote;
            s.NewLast += OnLast;
            s.NewDayBar += OnDayBar;
        }

        private void Unsubscribe(Symbol s)
        {
            s.NewQuote -= OnQuote;
            s.NewLast -= OnLast;
            s.NewDayBar -= OnDayBar;
        }

        // Handlers only keep the subscriptions alive; values are read from the Symbol on each recalculation.
        private void OnQuote(Symbol symbol, Quote quote) { }
        private void OnLast(Symbol symbol, Last last) { }
        private void OnDayBar(Symbol symbol, DayBar dayBar) { }

        #endregion

        #region Calculation

        private void Tick()
        {
            if (Interlocked.Exchange(ref computing, 1) == 1)
                return;
            try
            {
                var snap = Compute();
                if (snap == null)
                    return;
                snapshot = snap;
                if (!sampleLogged && DateTime.UtcNow - loadedAt > TimeSpan.FromSeconds(10))
                {
                    sampleLogged = true;
                    LogSample();
                }
                if (Expiration == 0 && NeedsNextExpiry(snap))
                {
                    Reload();
                    return;
                }
                CurrentChart?.RedrawBuffer();
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log(ex, "[GEX Profile] calculation failed", LoggingLevel.Error, Symbol?.Connection?.Name);
            }
            finally
            {
                Interlocked.Exchange(ref computing, 0);
            }
        }

        // Auto expiration: roll forward once the shown expiry has passed, or skip it while it has no open interest.
        private bool NeedsNextExpiry(Snapshot snap)
        {
            var now = DateTime.UtcNow;
            if (now > snap.FirstExpiryUtc)
            {
                Log($"expiry {snap.FirstExpiryUtc:yyyy-MM-dd HH:mm} UTC has passed, reloading");
                emptySkips = 0;
                return true;
            }
            if (!snap.HasData && Source == SourceOpenInterest && hasLaterExpiry && emptySkips < MaxEmptySkips && now - loadedAt > EmptyExpiryGrace)
            {
                Log($"no open interest on expiry {snap.FirstExpiryUtc:yyyy-MM-dd} yet, moving to the next one");
                emptySkips++;
                return true;
            }
            return false;
        }

        private Snapshot Compute()
        {
            List<Leg> current;
            Symbol under;
            lock (legs)
            {
                current = legs.ToList();
                under = underlying;
            }
            if (current.Count == 0 || under == null)
                return null;

            double f = PriceOf(under);
            if (!(f > 0))
                return null;

            var now = DateTime.UtcNow;
            double multiplier = Multiplier(under, f);
            bool useOi = Source == SourceOpenInterest;

            int n = current.Count;
            var qty = new double[n];
            var years = new double[n];
            var iv = new double[n];
            for (int i = 0; i < n; i++)
            {
                var leg = current[i];
                double q = useOi ? leg.Symbol.OpenInterest : leg.Symbol.Volume;
                qty[i] = double.IsNaN(q) || q < 0 ? 0 : q;
                years[i] = Math.Max((leg.ExpiryUtc - now).TotalDays, MinDaysToExpiry) / 365.0;
                iv[i] = GexMath.ImpliedVol(leg.IsCall, OptionPrice(leg.Symbol), f, leg.Strike, years[i]);
            }

            // Strikes without a usable quote borrow the IV of the nearest quoted strike in the same expiry.
            foreach (var expiry in Enumerable.Range(0, n).GroupBy(i => current[i].ExpiryUtc))
            {
                var idx = expiry.ToList();
                GexMath.FillMissingIv(idx.Select(i => (current[i].Strike, iv[i])).ToList(), (j, v) => iv[idx[j]] = v);
            }

            var rows = new SortedDictionary<double, StrikeRow>();
            for (int i = 0; i < n; i++)
            {
                var leg = current[i];
                if (!rows.TryGetValue(leg.Strike, out var row))
                    rows[leg.Strike] = row = new StrikeRow { Strike = leg.Strike };

                double gex = double.IsNaN(iv[i]) ? 0 : GexMath.Gex(leg.IsCall, GexMath.Gamma(f, leg.Strike, years[i], iv[i]), qty[i], multiplier, f);
                if (leg.IsCall)
                {
                    row.CallQty += qty[i];
                    row.CallGex += gex;
                    row.CallIv = iv[i];
                }
                else
                {
                    row.PutQty += qty[i];
                    row.PutGex += gex;
                    row.PutIv = iv[i];
                }
            }

            var list = rows.Values.ToList();
            bool split = Histogram == HistogramSplit;
            Func<StrikeRow, double> callMeasure = split ? r => r.CallGex : r => r.NetGex;
            Func<StrikeRow, double> putMeasure = split ? r => r.PutGex : r => r.NetGex;

            double step = double.MaxValue;
            for (int i = 1; i < list.Count; i++)
                step = Math.Min(step, list[i].Strike - list[i - 1].Strike);

            double maxPain = GexMath.MaxPain(list);
            var first = current.Min(l => l.ExpiryUtc);
            int expiryCount = current.Select(l => l.ExpiryUtc).Distinct().Count();
            var firstEt = TimeZoneInfo.ConvertTimeFromUtc(first, Eastern);
            var todayEt = TimeZoneInfo.ConvertTimeFromUtc(now, Eastern);
            int dte = Math.Max(0, (firstEt.Date - todayEt.Date).Days);
            string date = firstEt.ToString("dd MMM", CultureInfo.InvariantCulture) + (expiryCount > 1 ? $" +{expiryCount - 1}" : "");

            return new Snapshot
            {
                Rows = list,
                StrikeStep = step == double.MaxValue ? 0 : step,
                MaxPain = maxPain,
                CallWalls = list.Where(r => callMeasure(r) > 0).OrderByDescending(callMeasure).Take(WallsPerSide).Select(r => (r.Strike, callMeasure(r))).ToList(),
                PutWalls = list.Where(r => putMeasure(r) < 0).OrderBy(putMeasure).Take(WallsPerSide).Select(r => (r.Strike, putMeasure(r))).ToList(),
                Header = $"{date} · {dte} DTE · {n} opts · MP {FormatStrike(maxPain)}",
                HasData = qty.Any(q => q > 0),
                FirstExpiryUtc = first,
            };
        }

        private void LogSample()
        {
            List<Leg> current;
            Symbol under;
            lock (legs)
            {
                current = legs.ToList();
                under = underlying;
            }
            double f = PriceOf(under);
            Log($"underlying {under?.Name} price={f}; {current.Count(l => l.Symbol.OpenInterest > 0)} options with OI, {current.Count(l => l.Symbol.Volume > 0)} with volume, {current.Count(l => !double.IsNaN(OptionPrice(l.Symbol)))} with a price");
            foreach (var leg in current.OrderBy(l => Math.Abs(l.Strike - f)).Take(4))
            {
                var s = leg.Symbol;
                Log($"  {s.Name} K={leg.Strike} {(leg.IsCall ? "C" : "P")} OI={s.OpenInterest} vol={s.Volume} bid={s.Bid} ask={s.Ask} last={s.Last} delta={s.Delta} gamma={s.Gamma}");
            }
        }

        private static double PriceOf(Symbol s)
        {
            if (s == null)
                return double.NaN;
            if (s.Last > 0)
                return s.Last;
            if (s.Bid > 0 && s.Ask > 0)
                return (s.Bid + s.Ask) / 2;
            return double.NaN;
        }

        private static double OptionPrice(Symbol s)
        {
            if (s.Bid > 0 && s.Ask >= s.Bid)
                return (s.Bid + s.Ask) / 2;
            if (s.Last > 0)
                return s.Last;
            return double.NaN;
        }

        // Dollars per one point of the underlying (GC = 100). Derived from tick size and tick value.
        private static double Multiplier(Symbol s, double price)
        {
            try
            {
                double tick = s.GetTickSize(price), cost = s.GetTickCost(price);
                if (tick > 0 && cost > 0 && !double.IsNaN(cost))
                    return cost / tick;
            }
            catch
            {
            }
            return 100;
        }

        // COMEX metals options expire at 12:30 CT (13:30 ET). Used when the feed only gives a date.
        private static DateTime ExpiryUtc(DateTime exp)
        {
            if (exp.TimeOfDay != TimeSpan.Zero)
                return exp.Kind == DateTimeKind.Local ? exp.ToUniversalTime() : DateTime.SpecifyKind(exp, DateTimeKind.Utc);
            var et = DateTime.SpecifyKind(exp.Date.Add(new TimeSpan(13, 30, 0)), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(et, Eastern);
        }

        private static TimeZoneInfo FindEastern()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }

        #endregion

        #region Painting

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            var chart = CurrentChart;
            if (chart == null)
                return;

            var g = args.Graphics;
            var rect = args.Rectangle;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.SetClip(rect);

                var snap = snapshot;
                if (snap == null || snap.Rows.Count == 0)
                {
                    DrawHeader(g, rect, status ?? "", Color.Orange);
                    return;
                }

                var conv = chart.MainWindow.CoordinatesConverter;
                DrawHistogram(g, rect, conv, snap);
                DrawLevels(g, rect, conv, snap);
                DrawHeader(g, rect, snap.HasData ? snap.Header : status ?? snap.Header, snap.HasData ? CallColor : Color.Orange);
            }
            finally
            {
                g.Restore(state);
            }
        }

        private void DrawHistogram(Graphics g, Rectangle rect, IChartWindowCoordinatesConverter conv, Snapshot snap)
        {
            bool split = Histogram == HistogramSplit;
            float region = rect.Width * ProfileWidth / 100f;
            float axisX = Position == PositionRight ? rect.Right - region : rect.Left + region;

            double maxAbs = split
                ? snap.Rows.Max(r => Math.Max(Math.Abs(r.CallGex), Math.Abs(r.PutGex)))
                : snap.Rows.Max(r => Math.Abs(r.NetGex));
            if (maxAbs <= 0)
                return;
            double scale = (region - 4) / maxAbs;

            float thickness = 8;
            if (snap.StrikeStep > 0)
            {
                double y0 = conv.GetChartY(snap.Rows[0].Strike), y1 = conv.GetChartY(snap.Rows[0].Strike + snap.StrikeStep);
                thickness = (float)Math.Clamp(Math.Abs(y1 - y0) * 0.65, 2, 20);
            }

            using (var axisPen = new Pen(AxisColor))
                g.DrawLine(axisPen, axisX, rect.Top, axisX, rect.Bottom);

            // (row, value) pairs that get a value label: the biggest few on each side.
            var bars = split
                ? snap.Rows.Select(r => (r.Strike, value: r.CallGex)).Concat(snap.Rows.Select(r => (r.Strike, value: r.PutGex))).ToList()
                : snap.Rows.Select(r => (r.Strike, value: r.NetGex)).ToList();
            var labeled = new HashSet<(double, double)>(
                bars.Where(b => b.value > 0).OrderByDescending(b => b.value).Take(LabelsPerSide)
                    .Concat(bars.Where(b => b.value < 0).OrderBy(b => b.value).Take(LabelsPerSide)));

            using var callBrush = new SolidBrush(CallColor);
            using var putBrush = new SolidBrush(PutColor);
            using var textBrush = new SolidBrush(TextColor);
            using var darkBrush = new SolidBrush(Color.FromArgb(20, 24, 32));

            foreach (var (strike, value) in bars)
            {
                if (value == 0)
                    continue;
                float y = (float)conv.GetChartY(strike);
                if (y < rect.Top - thickness || y > rect.Bottom + thickness)
                    continue;

                float len = Math.Max(2f, (float)(Math.Abs(value) * scale));
                float x = value > 0 ? axisX : axisX - len;
                FillRoundedBar(g, value > 0 ? callBrush : putBrush, x, y - thickness / 2, len, thickness);

                if (!labeled.Contains((strike, value)))
                    continue;
                string text = GexMath.FormatGex(value);
                var size = g.MeasureString(text, LabelFont);
                float ty = y - size.Height / 2;
                float outside = value > 0 ? x + len + 2 : x - size.Width - 2;
                if (outside >= rect.Left && outside + size.Width <= rect.Right)
                    g.DrawString(text, LabelFont, textBrush, outside, ty);
                else if (len > size.Width + 8)
                    g.DrawString(text, LabelFont, darkBrush, value > 0 ? x + len - size.Width - 4 : x + 4, ty);
            }
        }

        private void DrawLevels(Graphics g, Rectangle rect, IChartWindowCoordinatesConverter conv, Snapshot snap)
        {
            for (int i = 0; i < snap.CallWalls.Count; i++)
            {
                var (strike, value) = snap.CallWalls[i];
                DrawLevel(g, rect, conv, strike, CallColor, false, i == 0 || WallLabels ? $"CALL WALL {FormatStrike(strike)} · {GexMath.FormatGex(value)}" : null);
            }
            for (int i = 0; i < snap.PutWalls.Count; i++)
            {
                var (strike, value) = snap.PutWalls[i];
                DrawLevel(g, rect, conv, strike, PutColor, false, i == 0 || WallLabels ? $"PUT WALL {FormatStrike(strike)} · {GexMath.FormatGex(value)}" : null);
            }
            if (!double.IsNaN(snap.MaxPain))
                DrawLevel(g, rect, conv, snap.MaxPain, MaxPainColor, true, $"MAX PAIN {FormatStrike(snap.MaxPain)}");
        }

        private static void DrawLevel(Graphics g, Rectangle rect, IChartWindowCoordinatesConverter conv, double price, Color color, bool dashed, string tag)
        {
            float y = (float)conv.GetChartY(price);
            if (y < rect.Top || y > rect.Bottom)
                return;

            using (var pen = new Pen(color, 1f) { DashStyle = dashed ? DashStyle.Dash : DashStyle.Solid })
                g.DrawLine(pen, rect.Left, y, rect.Right, y);

            if (tag == null)
                return;
            var size = g.MeasureString(tag, LabelFont);
            var box = new RectangleF(rect.Right - size.Width - 12, y - size.Height / 2 - 2, size.Width + 8, size.Height + 4);
            using (var path = RoundedRect(box, 3))
            using (var fill = new SolidBrush(Color.FromArgb(200, Darken(color))))
            using (var border = new Pen(color))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            using var text = new SolidBrush(Color.White);
            g.DrawString(tag, LabelFont, text, box.X + 4, box.Y + 2);
        }

        private static void DrawHeader(Graphics g, Rectangle rect, string text, Color dot)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var size = g.MeasureString(text, HeaderFont);
            float x = rect.Right - size.Width - 12, y = rect.Top + 8;
            using (var dotBrush = new SolidBrush(dot))
                g.FillEllipse(dotBrush, x - 12, y + size.Height / 2 - 3.5f, 7, 7);
            using var brush = new SolidBrush(TextColor);
            g.DrawString(text, HeaderFont, brush, x, y);
        }

        private static void FillRoundedBar(Graphics g, Brush brush, float x, float y, float w, float h)
        {
            if (w <= h || h < 4)
            {
                g.FillRectangle(brush, x, y, w, h);
                return;
            }
            using var path = new GraphicsPath();
            path.AddArc(x, y, h, h, 90, 180);
            path.AddArc(x + w - h, y, h, h, 270, 180);
            path.CloseFigure();
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Darken(Color c) => Color.FromArgb(c.R / 2, c.G / 2, c.B / 2);

        #endregion

        private static string FormatStrike(double strike) => strike.ToString("0.##", CultureInfo.InvariantCulture);

        private void Log(string message) =>
            Core.Instance.Loggers.Log("[GEX Profile] " + message, LoggingLevel.System, Symbol?.Connection?.Name);
    }
}
