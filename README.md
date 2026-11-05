# GEX Profile – Quantower Indicator

GEX Profile is a Quantower indicator. It reads the option chain of the chart's futures contract (built and tested on COMEX metals options through Rithmic), works out the **gamma exposure (GEX)** at every strike, and draws it as a horizontal histogram on the price chart. It also draws key levels taken from the chain: **call wall, put wall, peak gamma, zero gamma (gamma flip) and max pain**.

Everything is calculated live from the option quotes, open interest and volume that your data connection supplies. No external data service is used.

---

## Contents

1. [What you see on the chart](#1-what-you-see-on-the-chart)
2. [Settings reference](#2-settings-reference)
3. [On-chart mode buttons](#3-on-chart-mode-buttons)
4. [How the option chain is loaded](#4-how-the-option-chain-is-loaded)
5. [How everything is calculated](#5-how-everything-is-calculated)
6. [How things are drawn](#6-how-things-are-drawn)
7. [Header and status messages](#7-header-and-status-messages)
8. [Refresh, expiry roll-over and lifecycle](#8-refresh-expiry-roll-over-and-lifecycle)
9. [Logging](#9-logging)
10. [Building and installing](#10-building-and-installing)
11. [Assumptions and limitations](#11-assumptions-and-limitations)

---

## 1. What you see on the chart

| Element | Description |
|---|---|
| **GEX histogram** | One horizontal bar per strike, placed at that strike's price. Positive GEX bars go to the right of a vertical centre line and negative bars go to the left. |
| **Centre line** | A faint vertical line marking zero GEX. The histogram, level lines and tags all line up with it. |
| **Value labels** | The GEX value (e.g. `+12.4M`) on the 3 largest positive and 3 largest negative bars. |
| **Call wall(s)** | Horizontal line(s) at the strike(s) with the strongest positive GEX. |
| **Put wall(s)** | Horizontal line(s) at the strike(s) with the strongest negative GEX. |
| **Peak gamma** | Line at the strike with the most gamma from calls and puts together. |
| **Zero gamma** | Line at the underlying price where total GEX for the whole chain changes sign (the gamma flip). |
| **Max pain** | Line at the strike where option holders' total payout at expiry would be smallest. |
| **Level tags** | Small coloured name tags ("CALL WALL", "PUT WALL 2", "MAX PAIN"…) under each level line. |
| **Header** | Expiry date, days to expiry, number of options loaded, and Σ total GEX of the visible strikes. |
| **Mode buttons** | Two rows of buttons under the header for switching histogram type, data source and expiration. |

The indicator draws in the chart background (behind the candles), on the main price panel.

---

## 2. Settings reference

The settings are split into five groups. Defaults are shown in **bold**.

### 2.1 GEX

| Setting | Values | Default | What it does |
|---|---|---|---|
| **Source** | Open Interest / Day Volume (live positioning) | **Open Interest** | The quantity GEX is weighted by. *Open Interest* is the number of open contracts, which CME publishes once a day. *Day Volume* is today's traded contracts, which updates live during the session and shows where today's activity is. The same quantity is used for max pain, walls, peak gamma and zero gamma. |
| **Histogram** | Net (one bar per strike) / Split (calls right, puts left) | **Net** | *Net*: one bar per strike, equal to call GEX + put GEX. It goes right if positive and left if negative. *Split*: two bars per strike, call GEX to the right and put GEX to the left. This also changes how walls are chosen (see [5.8](#58-call-walls-and-put-walls)). |
| **Walls per side** | 1 – 10 | **1** | How many call walls and how many put walls to draw. With 3, you get the three strongest positive strikes and the three strongest negative strikes. |
| **Secondary wall labels** | on / off | **off** | Only matters when *Walls per side* is above 1. The strongest wall on each side always gets a tag ("CALL WALL" / "PUT WALL"). When this is on, the other walls get numbered tags too ("CALL WALL 2", "CALL WALL 3", "PUT WALL 2"…). When it's off, they get a line only. |
| **Mode buttons** | on / off | **on** | Shows or hides the on-chart buttons (see [section 3](#3-on-chart-mode-buttons)). |

### 2.2 Profile

| Setting | Values | Default | What it does |
|---|---|---|---|
| **Right padding (% of chart width)** | 0 – 80 | **0** | Empty space between the right edge of the chart and the histogram, as a percentage of chart width. Use it to move the profile left, away from the price scale or recent candles. |
| **Profile width (%)** | 5 – 80 | **20** | Width of the histogram area as a percentage of chart width. The longest bar fills this width minus 4 px, and every other bar is scaled to it. The centre line sits at the left edge of this area, so negative bars extend left of it into the price area. |
| **Positive GEX color** | colour | **#00D1A0** (teal) | Colour of positive bars: net-positive bars in Net mode, call bars in Split mode. |
| **Negative GEX color** | colour | **#9650E6** (purple) | Colour of negative bars: net-negative bars in Net mode, put bars in Split mode. |
| **Node thickness (% of strike spacing)** | 5 – 100 | **10** | Bar height as a percentage of the on-screen distance between two neighbouring strikes. 100 % makes bars touch, and lower values make thinner bars. Height is always kept between 1 and 60 px, so bars resize as you zoom the price scale. |

### 2.3 Levels

Every level has two settings:

- **A line setting** with a checkbox (line on/off), colour, width and style (Solid, Dash, Dot, DashDot; any other style draws solid).
- **A label setting** that shows or hides the level's name tag.

The two are independent: you can show a tag without its line, or a line without its tag.

| Setting | Default line | Default label | Level |
|---|---|---|---|
| **Call wall** / **Call wall label** | on, **#00D1A0**, width 1, **Solid** | **on** | Strongest positive GEX strike(s). |
| **Put wall** / **Put wall label** | on, **#9650E6**, width 1, **Solid** | **on** | Strongest negative GEX strike(s). |
| **Peak gamma** / **Peak gamma label** | on, **#3C8CFF** (blue), width 1, **Solid** | **on** | Strike with the largest \|call GEX\| + \|put GEX\|. |
| **Zero gamma** / **Zero gamma label** | on, **#FF9628** (orange), width 1, **Dash** | **on** | Gamma flip price for the whole chain. |
| **Max pain** / **Max pain label** | on, **#E64848** (red), width 1, **Dash** | **on** | Max pain strike for the whole chain. |

*Call wall label* and *Put wall label* control every wall tag on that side, including numbered secondary ones.

### 2.4 Labels

These settings style the level tags.

| Setting | Values | Default | What it does |
|---|---|---|---|
| **Label text color** | colour | **White** | Text colour inside level tags. |
| **Label text size** | 2 – 20 | **8** | Font size (Segoe UI) of level tags. |
| **Label background** | Line color (darker) / Custom color | **Line color (darker)** | *Line color (darker)*: the tag fill is the level's line colour at half brightness, slightly transparent (alpha 200). *Custom color*: every tag uses the colour below. The tag border always uses the level's line colour. |
| **Label background color** | colour | **#1E222C** | Tag fill colour, used only when *Label background* is *Custom color*. |

### 2.5 Options

| Setting | Values | Default | What it does |
|---|---|---|---|
| **Expiration** | Auto (nearest) / This week / This month (to monthly OG) | **Auto** | Which expiries are loaded (see [4.3](#43-choosing-expiries)). *Auto*: only the nearest upcoming expiry. *This week*: every expiry up to the end of the nearest expiry's Monday–Friday week. *This month*: every expiry up to and including the next monthly expiry (the series without a week number, e.g. "OG"). |
| **Strikes per side** | 1 – 500 | **30** | How many strikes above and below the current price are shown in the histogram. The histogram, walls, peak gamma and Σ total use this window. Max pain and zero gamma always use the whole chain. |
| **Wide range (all strikes)** | on / off | **off** | When on, *Strikes per side* is ignored and every strike in the chain is used for the histogram, walls, peak gamma and Σ total. |

Changing any setting in the settings window recalculates the indicator. Changing *Expiration* reloads the option chain.

---

## 3. On-chart mode buttons

These buttons appear when **Mode buttons** is on. There are two rows, right-aligned 8 px left of the centre line and starting 30 px below the top of the chart:

| Row | Buttons | Same as setting |
|---|---|---|
| 1 | **Net** · **Split** · **OI** · **VOL** | Histogram (Net/Split), Source (Open Interest / Day Volume) |
| 2 | **Auto** · **Week** · **Month** | Expiration (Auto / This week / This month) |

- The active choice is highlighted with a teal fill and border. Inactive buttons are dark grey.
- Only a **left click** works. A click on a button is consumed, so it doesn't also act on the chart.
- Clicking the button that's already active does nothing.
- **Net / Split / OI / VOL** recalculate immediately. Nothing is reloaded.
- **Auto / Week / Month** drop the current chain and load the new set of expiries. The "skip empty expiry" counter is reset (see [4.3](#43-choosing-expiries)).
- A click changes the same value as the matching setting, so the settings window shows the new value afterwards.

---

## 4. How the option chain is loaded

The chain is loaded in the background when the indicator is added, when the chart symbol changes, when *Expiration* changes, and when an expiry rolls over.

### 4.1 Finding the option series

1. The indicator asks Quantower for the option series of the **chart symbol**. If there are none, it asks again for the chart symbol's **underlier** (e.g. the futures root).
2. **Binary/event contracts** (CME event contracts, which also come back as option series) are removed, because they are not gamma-bearing options.
3. If the request returns options on several futures months, only the series whose underlier is **the chart's own contract** are kept. If none match, all series are used.

### 4.2 Expiry date and time

- If the feed gives an expiry time, that time is used.
- If the feed gives only a date, the expiry is set to **13:30 US Eastern (12:30 Central)**, the standard COMEX metals options expiry time. Eastern time includes daylight saving.
- Expiries that have already passed are ignored.

### 4.3 Choosing expiries

The upcoming series are grouped by expiry date and sorted, nearest first.

| Mode | Expiries loaded |
|---|---|
| **Auto (nearest)** | The single nearest upcoming expiry. |
| **This week** | Every expiry from the nearest one up to (not including) the Monday after it. Once Friday's expiry has passed, the nearest expiry falls in the next week, so the window moves forward automatically. |
| **This month** | Every expiry from the nearest one up to and including the first **monthly** expiry. A series counts as monthly when its name has no digits (e.g. "OG"); weeklies carry a week number (e.g. "OG1", "G2M"). If no monthly is left, the status shows "No monthly expiry left on …". |

**Skipping empty expiries (Auto mode only).** Newly listed expiries have no open interest until CME publishes it. In *Auto* mode with *Source = Open Interest*, if every option in the loaded expiry still has zero OI **20 seconds** after loading, and a later expiry exists, the indicator moves on to the next expiry. It does this at most **3** times in a row. The counter resets when an expiry passes or when you change the expiration mode.

**Several series on the same date.** If more than one series expires on the same day, only the one with the **most option symbols** is used, so no contract is counted twice.

### 4.4 Underlying price

- The underlying is the option's underlier as reported by the feed. If it doesn't report one, the chart symbol is used. If the underlier is a different symbol from the chart, the indicator subscribes to it for its price.
- **Price used:** last trade price; if there is none, the mid of bid and ask.
- When loading, the indicator waits up to **15 seconds** (60 checks, 250 ms apart) for a price. If none arrives, it shows "No price for …".

### 4.5 Subscriptions

- **Every strike** of every chosen expiry is loaded and subscribed (quotes, trades and day bars), not just the visible window. Max pain and zero gamma need the whole chain.
- Values (bid, ask, last, OI, volume) are read from each option symbol on every recalculation.

---

## 5. How everything is calculated

The calculation runs **every 1 second**, starting 0.5 s after the chain is loaded. If a calculation is still running when the next one is due, the next one is skipped.

### 5.1 Quantity per option

- **Open Interest** source: the option's open interest.
- **Day Volume** source: the option's volume for the day.
- Missing (NaN) or negative values count as 0.

### 5.2 Time to expiry

```
T = max(expiry − now, 15 minutes) / 365 days      (in years, calendar days)
```

The 15-minute floor keeps gamma finite for options at or just past expiry.

### 5.3 Option price

1. **Mid** = (bid + ask) / 2, when bid > 0 and ask ≥ bid.
2. Otherwise the **last** trade price, if above 0.
3. Otherwise there is no price, so no IV can be calculated from this option's own quote (see 5.4).

### 5.4 Implied volatility (Black-76)

Options on futures are priced with **Black-76**, with the interest rate r = 0:

```
d1 = [ ln(F/K) + ½·σ²·T ] / (σ·√T)
d2 = d1 − σ·√T
Call = F·N(d1) − K·N(d2)
Put  = K·N(−d2) − F·N(−d1)
```

- F = underlying futures price, K = strike, σ = volatility, N = standard normal CDF. N is calculated with the Numerical Recipes erfc approximation, which has an error below 1.2 × 10⁻⁷.
- **IV is found by bisection** between **0.1 % and 500 %**, over 80 iterations, so it's effectively exact.
- An option gets **no IV** when:
  - it has no price,
  - its price is at or below intrinsic value (no time value), or
  - its price is outside what 0.1 %–500 % volatility can produce.

**Filling missing IVs.** Within each expiry, an option without an IV borrows the IV of the **nearest strike** in the same expiry that has one. Calls and puts are pooled for this. If no option in the expiry has an IV, that expiry's options contribute no GEX.

### 5.5 Gamma

```
Γ = φ(d1) / (F · σ · √T)
```

φ is the standard normal density. Gamma is 0 when F, K, T or σ is not positive.

### 5.6 Gamma exposure (GEX)

```
GEX = ± Γ × quantity × multiplier × F² × 0.01
```

- **Meaning:** the dollar change in dealers' hedge (delta) for a **1 % move** in the underlying. This is the standard "dollar gamma per 1 %" convention.
- **Sign:** calls count **positive** and puts count **negative**. This is the usual market assumption that dealers are long the calls and short the puts customers trade.
- **Multiplier:** dollars per one point of the underlying, worked out as *tick value ÷ tick size* of the underlying (e.g. GC: $10 ÷ 0.1 = **100**, MGC = 10). If the platform can't supply it, **100** is used.

### 5.7 Per-strike rows and the visible window

- All options are grouped by **strike**. When several expiries are loaded, each strike adds up call GEX, put GEX, call quantity and put quantity across every expiry.
- **Net GEX** of a strike = call GEX + put GEX (put GEX is already negative).
- **ATM strike** = the strike closest to the current underlying price, taken from all loaded expiries' strikes together.
- **Visible window** = *Strikes per side* strikes below the ATM strike, the ATM strike, and *Strikes per side* strikes above it (2N + 1 strikes, fewer at the edges of the chain). With *Wide range* on, it's every strike.
- **Strike spacing** = the smallest gap between neighbouring strikes in the window. It's used for bar thickness.

| Uses the **visible window** | Uses the **whole chain** |
|---|---|
| Histogram bars, value labels | Max pain |
| Call walls, put walls | Zero gamma |
| Peak gamma | |
| Σ total GEX in the header | |

Σ is kept on the visible window on purpose. It shows the balance of what's on screen, while zero gamma shows where the whole chain flips.

### 5.8 Call walls and put walls

| Histogram mode | Call wall | Put wall |
|---|---|---|
| **Net** | Strike(s) with the **largest positive net GEX** | Strike(s) with the **most negative net GEX** |
| **Split** | Strike(s) with the **largest call GEX** | Strike(s) with the **largest put GEX** (most negative) |

- Only strikes in the visible window count.
- Up to *Walls per side* walls are chosen on each side, strongest first. Wall 1 is the strongest, wall 2 the next, and so on.
- If no strike has a positive (or negative) value, that side has no wall. For example, in Net mode, when net GEX is positive on every visible strike there is no put wall.

### 5.9 Peak gamma

Peak gamma is the strike in the visible window with the largest **|call GEX| + |put GEX|**: the most total gamma, whatever the direction. Strikes with no GEX at all are ignored.

### 5.10 Zero gamma (gamma flip)

Zero gamma is the underlying price where **total GEX of the whole chain** changes sign.

1. The range searched runs from the **lowest strike** to the **highest strike** in the chain.
2. The range is split into **400 evenly spaced test prices**.
3. At each test price *S*, total GEX is recalculated **as if the underlying were at S**:
   - each option's IV and time to expiry are kept fixed;
   - gamma is recalculated at *S*, and *S²* replaces *F²* in the GEX formula;
   - only options with quantity > 0 and a valid IV are included.
4. Far from all strikes, short-dated gamma can come out as exactly 0. Those points are skipped, and a sign change is detected by comparing with the last non-zero point, so it can still be found across such gaps.
5. Each place where the sign changes is narrowed by **bisection** (up to 20 steps, until the bracket is under 0.05 points). The exact price is then found by **linear interpolation** between the two ends.
6. If there are several sign changes, the one **closest to the current price** is used.
7. If total GEX keeps the same sign across the whole range, there's no zero gamma and no line is drawn.

### 5.11 Max pain

Max pain is calculated on the **whole chain** (all strikes of all loaded expiries):

```
pain(P) = Σ over strikes K [ callQty(K) × max(0, P − K) + putQty(K) × max(0, K − P) ]
```

- Every strike is tried as the settlement price P. **Max pain** is the strike with the smallest pain.
- The quantity is the selected **Source**: open interest normally, today's volume when *Day Volume* is selected.
- Multiplier and premiums are left out because they don't change which strike is lowest.

### 5.12 Σ total GEX

Σ is the sum of **net GEX of every strike in the visible window**. It's shown at the end of the header in green when ≥ 0 and red when negative.

### 5.13 Number formatting

GEX values are shortened with a sign:

| Size | Format | Example |
|---|---|---|
| ≥ 1 billion | 1 decimal + B | `+1.2B` |
| ≥ 1 million | 1 decimal + M | `-34.5M` |
| ≥ 1 thousand | no decimals + K | `+820K` |
| below | whole number | `-450` |

Zero has no sign.

---

## 6. How things are drawn

### 6.1 Layout

```
|<---------------- chart width ---------------->|
|  price area  ...  [tags] |  histogram  | pad  |
                           ^ centre line (zero GEX)
```

- **Histogram area width** = *Profile width* % of chart width.
- **Centre line position** = right edge − *Right padding* % − histogram width.
- Positive bars go right from the centre line. Negative bars go left into the price area.
- Everything is clipped to the chart panel.

### 6.2 Bars

- **Scale:** the bar with the largest absolute value (across both sides, or both call and put bars in Split mode) fills the histogram width minus 4 px. Every other bar is scaled to it.
- **Minimum length:** 2 px, so very small non-zero values stay visible. Strikes with exactly zero GEX get no bar.
- **Height:** (on-screen strike spacing) × *Node thickness* %, kept between **1 and 60 px**. It's 8 px when only one strike is visible.
- **Shape:** rounded ends when the bar is at least 4 px tall and longer than it is tall; otherwise a plain rectangle.
- Bars whose strike is off-screen vertically are skipped.

### 6.3 Value labels

- Shown on the **3 largest positive** and **3 largest negative** bars. In Split mode, call and put bars compete together.
- Font: Segoe UI 8, light grey (#DCE0E6).
- Placed **just outside** the bar's end (2 px gap). If that would go off the chart, the label goes **inside** the bar in dark text, but only when the bar is long enough to hold it. Otherwise the label is left out.

### 6.4 Level lines and tags

- Lines run from the **left edge of the chart to the centre line**, so they don't cross the histogram. A level whose price is off-screen is not drawn.
- **Drawing order:** peak gamma → call walls → put walls → max pain → zero gamma. Later levels are drawn on top of earlier ones.
- **Tag position:** just below the line, clear of the strike's bar. The gap is 4 px below the larger of half the line width and half the bar height. The tag's right edge sits 4 px left of the centre line.
- **Overlapping tags:** when a tag would overlap one already drawn, it moves left of that tag (4 px gap). This repeats until it fits, so tags on close levels sit side by side instead of stacking.
- **Tag look:** rounded box with the level's colour as border, filled according to *Label background*, and the text in *Label text color* / *Label text size*.
- **Tag names:** `PEAK GAMMA`, `CALL WALL`, `CALL WALL 2`…, `PUT WALL`, `PUT WALL 2`…, `MAX PAIN`, `ZERO GAMMA`.

---

## 7. Header and status messages

The header sits at the top of the chart and is right-aligned so it ends just left of the centre line. A coloured dot sits in front of it.

**With data** (teal dot):

```
● 07 Nov +2 · 2 DTE · 1240 opts · Σ +85.3M
```

| Part | Meaning |
|---|---|
| `07 Nov` | Nearest loaded expiry date (US Eastern). |
| `+2` | Number of extra expiries loaded (Week/Month modes). Left out when only one is loaded. |
| `2 DTE` | Days to the nearest expiry, counted in US Eastern calendar days (0 on expiry day). |
| `1240 opts` | Number of option contracts loaded: calls and puts, all strikes, all loaded expiries. |
| `Σ +85.3M` | Total net GEX of the visible window: green when ≥ 0, red when negative. |

**Status messages** (orange dot) are shown while loading or when something is missing:

| Message | Meaning |
|---|---|
| Loading option chain… | The chain is being loaded. |
| Waiting for option data… | Options are subscribed but no OI/volume has arrived yet (any quantity counts as data). |
| No chart symbol | The chart has no symbol. |
| No option series found for … | The connection lists no options for this symbol or its underlier. |
| No upcoming expirations | Every expiry has already passed. |
| No monthly expiry left on … | *This month* mode, but no monthly series is left. |
| Option chain is empty | The series exist but contain no option symbols. |
| No price for … | No last or bid/ask price for the underlying within 15 s. |
| Error: … | Loading failed. Details are written to the Quantower log. |

---

## 8. Refresh, expiry roll-over and lifecycle

- **Recalculation:** every 1 s, then the chart is redrawn.
- **Expiry roll-over:** as soon as the nearest loaded expiry passes its expiry time, the chain is reloaded. *Auto* moves to the next expiry; *This week* / *This month* drop the expired one.
- **Empty-expiry skip:** see [4.3](#43-choosing-expiries).
- **Settings change / symbol change:** the indicator is reset and the chain is loaded again.
- **Removing the indicator:** loading is cancelled, the timer is stopped, and all option and underlying subscriptions and the chart click handler are removed.

---

## 9. Logging

Diagnostic messages are written to the Quantower log, prefixed `[GEX Profile]`:

- the chart symbol, its type and underlier;
- every option series found (name, expiry, type, underlier);
- which series was used for each expiry and how many option symbols it has;
- the strike count and UTC expiry time per series;
- how many options were subscribed and which multiplier is used;
- **10 s after loading**, one sample: the underlying price, how many options have OI / volume / a price, the 4 options nearest the money (OI, volume, bid, ask, last, feed delta and gamma), the zero gamma result and range searched, max pain, and total GEX;
- expiry roll-overs and empty-expiry skips;
- errors, with full exception details.

---

## 10. Building and installing

**Requirements**

- Windows, .NET SDK 10.
- Quantower installed (default path `C:\Quantower`, built against version `v1.147.9`).
- A connection that supplies the **option chain with open interest** for the futures contract (e.g. Rithmic for COMEX).

**Build**

```
dotnet build GexProfile
```

The project builds straight into Quantower's indicator folder:

```
C:\Quantower\Settings\Scripts\Indicators\GexProfile\
```

If your Quantower path or version is different:

```
dotnet build GexProfile -p:QuantowerRoot="D:\Quantower" -p:QuantowerVersion=v1.148.0
```

**Use**

1. Restart Quantower (or reload scripts), open a chart of the futures contract (e.g. GC), and add **GEX Profile** from the indicator list.
2. Wait for the header to change from "Loading option chain…" to the expiry line. Open interest can take a few seconds to arrive.

**Files**

| File | Contents |
|---|---|
| `GexProfile/GexProfileIndicator.cs` | The indicator: settings, chain loading, calculation loop, drawing, mode buttons. |
| `GexProfile/GexMath.cs` | Math: Black-76 price, implied vol, gamma, GEX, zero gamma search, max pain, number formatting, IV filling. |
| `GexProfile/GexProfile.csproj` | Project file (target framework, Quantower reference, output folder). |
| `Reference screenshots/` | The original reference images used as the visual spec. |

---

## 11. Assumptions and limitations

- **Dealer positioning is assumed, not known.** GEX assumes dealers are long calls and short puts. Real positioning can differ, so treat the levels as estimates.
- **Open interest updates once a day.** CME publishes OI after each session, so intraday changes are not reflected. Use *Day Volume* to see today's activity. Volume counts trades, though, not net open positions.
- **Interest rate = 0.** Black-76 is used without discounting. For short-dated futures options the effect is negligible.
- **IV comes from each option's own mid price.** Strikes without a usable quote borrow the nearest strike's IV in the same expiry. Wide or stale quotes affect the IV and therefore the gamma.
- **Expiry time defaults to 13:30 ET** when the feed gives only a date. That's correct for COMEX metals; other products may expire at a different time.
- **Monthly detection depends on series names.** A series counts as monthly when its name has no digits.
- **Expired or nearly expired options** use a 15-minute minimum time to expiry.
- Σ total, walls and peak gamma depend on *Strikes per side* / *Wide range*. Max pain and zero gamma never do.
