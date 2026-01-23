namespace Celeste.Mod.MiaoNet;

public static class UniqueMatcher
{
    private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    public static T? MatchBy<T>(IEnumerable<T> items, Func<T, string> selector, string value)
        where T : class
    {
        if (string.IsNullOrEmpty(value)) return null;

        T? eq = null, sw = null, ct = null;
        int eqCount = 0, swCount = 0, ctCount = 0;

        foreach (var item in items)
        {
            string s = selector(item);

            if (s.Equals(value, Comparison))
            {
                eqCount++;
                eq = item;
            }

            if (s.StartsWith(value, Comparison))
            {
                swCount++;
                sw = item;
            }

            if (s.Contains(value, Comparison))
            {
                ctCount++;
                ct = item;
            }
        }

        if (eqCount == 1) return eq;
        if (swCount == 1) return sw;
        return ctCount == 1 ? ct : null;
    }

    public static T? MatchBy<T>(IEnumerable<T> items, Func<T, IEnumerable<string>> selector, string value)
        where T : class
    {
        if (string.IsNullOrEmpty(value)) return null;

        T? eq = null, sw = null, ct = null;
        int eqCount = 0, swCount = 0, ctCount = 0;

        foreach (var item in items)
        {
            bool hasEq = false, hasSw = false, hasCt = false;

            foreach (var s in selector(item))
            {
                if (!hasEq && s.Equals(value, Comparison)) hasEq = true;
                if (!hasSw && s.StartsWith(value, Comparison)) hasSw = true;
                if (!hasCt && s.Contains(value, Comparison)) hasCt = true;
            }

            if (hasEq) { eqCount++; eq = item; }
            if (hasSw) { swCount++; sw = item; }
            if (hasCt) { ctCount++; ct = item; }
        }

        if (eqCount == 1) return eq;
        if (swCount == 1) return sw;
        return ctCount == 1 ? ct : null;
    }
}
