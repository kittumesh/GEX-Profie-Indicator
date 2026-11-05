using System;
using System.Collections.Generic;
using System.Linq;

namespace GexProfile
{
    /// <summary>Black-76 pricing (options on futures, r = 0) and GEX helpers.</summary>
    public static class GexMath
    {
        private const double MinVol = 0.001;
        private const double MaxVol = 5.0;

        public static double NormPdf(double x) => Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);

        public static double NormCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2));

        // Numerical Recipes erfc, |error| < 1.2e-7.
        private static double Erfc(double x)
        {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                       t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                       t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? r : 2.0 - r;
        }

        public static double Price(bool isCall, double f, double k, double t, double vol)
        {
            double sqrtT = Math.Sqrt(t);
            double d1 = (Math.Log(f / k) + 0.5 * vol * vol * t) / (vol * sqrtT);
            double d2 = d1 - vol * sqrtT;
            return isCall
                ? f * NormCdf(d1) - k * NormCdf(d2)
                : k * NormCdf(-d2) - f * NormCdf(-d1);
        }

        public static double Gamma(double f, double k, double t, double vol)
        {
            if (f <= 0 || k <= 0 || t <= 0 || vol <= 0)
                return 0;
            double sqrtT = Math.Sqrt(t);
            double d1 = (Math.Log(f / k) + 0.5 * vol * vol * t) / (vol * sqrtT);
            return NormPdf(d1) / (f * vol * sqrtT);
        }

        /// <summary>Implied vol by bisection. Returns NaN when the price carries no usable time value.</summary>
        public static double ImpliedVol(bool isCall, double price, double f, double k, double t)
        {
            if (double.IsNaN(price) || price <= 0 || f <= 0 || k <= 0 || t <= 0)
                return double.NaN;

            double intrinsic = Math.Max(0, isCall ? f - k : k - f);
            if (price <= intrinsic + 1e-9 || price < Price(isCall, f, k, t, MinVol) || price > Price(isCall, f, k, t, MaxVol))
                return double.NaN;

            double lo = MinVol, hi = MaxVol;
            for (int i = 0; i < 80; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Price(isCall, f, k, t, mid) > price)
                    hi = mid;
                else
                    lo = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>Dollar gamma per 1% move: Γ · qty · multiplier · F² · 0.01. Calls positive, puts negative.</summary>
        public static double Gex(bool isCall, double gamma, double qty, double multiplier, double f)
            => (isCall ? 1 : -1) * gamma * qty * multiplier * f * f * 0.01;

        /// <summary>
        /// Underlying price where total GEX changes sign (gamma flip). Total GEX is recomputed at hypothetical prices
        /// between lo and hi, keeping each option's IV and time fixed; the crossing nearest to spot is returned, or NaN if none.
        /// </summary>
        public static double GammaFlip(IReadOnlyList<(bool isCall, double strike, double years, double iv, double qty)> options,
                                       double multiplier, double spot, double lo, double hi, int points = 400)
        {
            if (options.Count == 0 || !(hi > lo) || points < 2)
                return double.NaN;

            double Total(double s)
            {
                double sum = 0;
                foreach (var o in options)
                    if (o.qty > 0 && !double.IsNaN(o.iv))
                        sum += Gex(o.isCall, Gamma(s, o.strike, o.years, o.iv), o.qty, multiplier, s);
                return sum;
            }

            // Far from every strike, short-dated gamma underflows to exactly 0; compare against the last non-zero
            // point so a sign change across such a stretch is still found.
            double best = double.NaN;
            double step = (hi - lo) / (points - 1);
            double prevS = lo, prev = Total(lo);
            for (int i = 1; i < points; i++)
            {
                double s = lo + i * step, cur = Total(s);
                if (cur == 0)
                    continue;
                if (prev != 0 && Math.Sign(prev) != Math.Sign(cur))
                {
                    // Over the whole chain the grid is coarse, so narrow the bracket by bisection before interpolating.
                    double a = prevS, b = s, fa = prev, fb = cur;
                    for (int k = 0; k < 20 && b - a > 0.05; k++)
                    {
                        double m = 0.5 * (a + b), fm = Total(m);
                        if (fm == 0)
                        {
                            a = b = m;
                            break;
                        }
                        if (Math.Sign(fm) == Math.Sign(fa))
                            (a, fa) = (m, fm);
                        else
                            (b, fb) = (m, fm);
                    }
                    double flip = a == b ? a : a + (b - a) * fa / (fa - fb);
                    if (double.IsNaN(best) || Math.Abs(flip - spot) < Math.Abs(best - spot))
                        best = flip;
                }
                prevS = s;
                prev = cur;
            }
            return best;
        }

        /// <summary>Strike at which option holders' total intrinsic payout is smallest.</summary>
        public static double MaxPain(IReadOnlyList<StrikeRow> rows)
        {
            double best = double.NaN, bestPain = double.MaxValue;
            foreach (var candidate in rows)
            {
                double p = candidate.Strike, pain = 0;
                foreach (var r in rows)
                    pain += r.CallQty * Math.Max(0, p - r.Strike) + r.PutQty * Math.Max(0, r.Strike - p);
                if (pain < bestPain)
                {
                    bestPain = pain;
                    best = p;
                }
            }
            return best;
        }

        public static string FormatGex(double v)
        {
            string sign = v > 0 ? "+" : v < 0 ? "-" : "";
            double a = Math.Abs(v);
            if (a >= 1e9) return $"{sign}{a / 1e9:0.0}B";
            if (a >= 1e6) return $"{sign}{a / 1e6:0.0}M";
            if (a >= 1e3) return $"{sign}{a / 1e3:0}K";
            return $"{sign}{a:0}";
        }

        /// <summary>Replaces missing IVs with the IV of the nearest strike that has one.</summary>
        public static void FillMissingIv(IList<(double strike, double iv)> points, Action<int, double> assign)
        {
            var valid = points.Where(p => !double.IsNaN(p.iv)).ToList();
            if (valid.Count == 0)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (!double.IsNaN(points[i].iv))
                    continue;
                double k = points[i].strike;
                assign(i, valid.OrderBy(p => Math.Abs(p.strike - k)).First().iv);
            }
        }
    }

    public sealed class StrikeRow
    {
        public double Strike;
        public double CallQty, PutQty;
        public double CallGex, PutGex;
        public double CallIv = double.NaN, PutIv = double.NaN;
        public double NetGex => CallGex + PutGex;
    }
}
