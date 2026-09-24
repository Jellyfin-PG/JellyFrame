using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    /// <summary>
    /// Evaluates target Jellyfin version constraints against a running server version.
    /// Supports wildcards (10.11.*, 10.*), ranges (>=10.10.0 <10.12.0), plus notation (10.10+),
    /// caret (^10.10.0), tilde (~10.10.2), and OR clauses (||).
    /// </summary>
    public static class VersionRangeMatcher
    {
        private static readonly Regex CleanVerRegex = new Regex(@"^v?(\d+(\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Checks if the current version satisfies the given target constraint.
        /// Returns true if constraint is null/empty or "*" (any version).
        /// </summary>
        public static bool IsCompatible(string constraint, Version currentVersion)
        {
            if (currentVersion == null)
                return true;

            if (string.IsNullOrWhiteSpace(constraint))
                return true;

            var trimmed = constraint.Trim();
            if (trimmed == "*" || string.Equals(trimmed, "any", StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle OR clauses (e.g., "10.10.* || 10.11.*")
            if (trimmed.Contains("||"))
            {
                var parts = trimmed.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (IsCompatibleSingleClause(part.Trim(), currentVersion))
                        return true;
                }
                return false;
            }

            return IsCompatibleSingleClause(trimmed, currentVersion);
        }

        /// <summary>
        /// Checks if the current version string satisfies the target constraint.
        /// </summary>
        public static bool IsCompatible(string constraint, string currentVersionString)
        {
            var parsed = ParseCleanVersion(currentVersionString);
            return IsCompatible(constraint, parsed);
        }

        private static bool IsCompatibleSingleClause(string clause, Version current)
        {
            if (string.IsNullOrWhiteSpace(clause))
                return true;

            // Multiple AND conditions (e.g. ">=10.10.0 <10.12.0" or ">=10.10.0, <10.12.0")
            var tokens = SplitConditions(clause);
            foreach (var token in tokens)
            {
                if (!EvaluateSingleCondition(token, current))
                    return false;
            }

            return true;
        }

        private static List<string> SplitConditions(string clause)
        {
            var result = new List<string>();
            var normalized = clause.Replace(",", " ").Trim();
            var parts = Regex.Split(normalized, @"\s+");

            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                if (string.IsNullOrEmpty(p)) continue;

                // Handle split operators like [">=", "10.10.0"]
                if ((p == ">=" || p == "<=" || p == ">" || p == "<" || p == "==" || p == "=") && i + 1 < parts.Length)
                {
                    result.Add(p + parts[i + 1].Trim());
                    i++;
                }
                else
                {
                    result.Add(p);
                }
            }

            return result;
        }

        private static bool EvaluateSingleCondition(string cond, Version current)
        {
            if (string.IsNullOrWhiteSpace(cond)) return true;

            cond = cond.Trim();
            if (cond == "*" || cond.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;

            // 1. Plus notation: "10.10+" or "10.10.2+"
            if (cond.EndsWith("+"))
            {
                var baseStr = cond.Substring(0, cond.Length - 1).Trim();
                var baseVer = ParseCleanVersion(baseStr);
                if (baseVer == null) return true;
                return CompareVersions(current, baseVer) >= 0;
            }

            // 2. Caret notation: "^10.10.0" -> >= 10.10.0 and < (Major+1).0.0
            if (cond.StartsWith("^"))
            {
                var baseStr = cond.Substring(1).Trim();
                var baseVer = ParseCleanVersion(baseStr);
                if (baseVer == null) return true;

                if (CompareVersions(current, baseVer) < 0) return false;

                int upperMajor = baseVer.Major > 0 ? baseVer.Major + 1 : 0;
                int upperMinor = baseVer.Major == 0 ? baseVer.Minor + 1 : 0;
                var upperVer = baseVer.Major > 0
                    ? new Version(upperMajor, 0, 0)
                    : new Version(0, upperMinor, 0);

                return CompareVersions(current, upperVer) < 0;
            }

            // 3. Tilde notation: "~10.10.2" -> >= 10.10.2 and < 10.11.0
            if (cond.StartsWith("~"))
            {
                var baseStr = cond.Substring(1).Trim();
                var baseVer = ParseCleanVersion(baseStr);
                if (baseVer == null) return true;

                if (CompareVersions(current, baseVer) < 0) return false;

                var upperVer = new Version(baseVer.Major, baseVer.Minor + 1, 0);
                return CompareVersions(current, upperVer) < 0;
            }

            // 4. Comparison operators: >=, <=, >, <, ==, =
            if (cond.StartsWith(">="))
            {
                var v = ParseCleanVersion(cond.Substring(2));
                return v != null && CompareVersions(current, v) >= 0;
            }
            if (cond.StartsWith("<="))
            {
                var v = ParseCleanVersion(cond.Substring(2));
                return v != null && CompareVersions(current, v) <= 0;
            }
            if (cond.StartsWith(">"))
            {
                var v = ParseCleanVersion(cond.Substring(1));
                return v != null && CompareVersions(current, v) > 0;
            }
            if (cond.StartsWith("<"))
            {
                var v = ParseCleanVersion(cond.Substring(1));
                return v != null && CompareVersions(current, v) < 0;
            }
            if (cond.StartsWith("=="))
            {
                var v = ParseCleanVersion(cond.Substring(2));
                return v != null && CompareVersions(current, v) == 0;
            }
            if (cond.StartsWith("="))
            {
                var v = ParseCleanVersion(cond.Substring(1));
                return v != null && CompareVersions(current, v) == 0;
            }

            // 5. Wildcards: "10.11.*", "10.11.x", "10.*", "10.x"
            if (cond.Contains("*") || cond.Contains("x") || cond.Contains("X"))
            {
                var parts = cond.Split('.');
                if (parts.Length >= 1 && parts[0] != "*" && !parts[0].Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(parts[0], out int major) && current.Major != major)
                        return false;
                }
                if (parts.Length >= 2 && parts[1] != "*" && !parts[1].Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(parts[1], out int minor) && current.Minor != minor)
                        return false;
                }
                if (parts.Length >= 3 && parts[2] != "*" && !parts[2].Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(parts[2], out int build) && current.Build >= 0 && current.Build != build)
                        return false;
                }
                return true;
            }

            // 6. Plain exact or prefix version (e.g. "10.11" or "10.11.0")
            var plainParts = cond.Split('.');
            if (plainParts.Length == 2)
            {
                // Two-part version like "10.11" behaves like "10.11.*"
                if (int.TryParse(plainParts[0], out int major) && current.Major != major)
                    return false;
                if (int.TryParse(plainParts[1], out int minor) && current.Minor != minor)
                    return false;
                return true;
            }

            var exact = ParseCleanVersion(cond);
            if (exact != null)
            {
                return CompareVersions(current, exact) == 0;
            }

            return true;
        }

        /// <summary>
        /// Compares two Version objects considering Major, Minor, and Build (ignoring Revision if negative or 0).
        /// </summary>
        public static int CompareVersions(Version a, Version b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
            if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);

            int aBuild = a.Build < 0 ? 0 : a.Build;
            int bBuild = b.Build < 0 ? 0 : b.Build;
            if (aBuild != bBuild) return aBuild.CompareTo(bBuild);

            int aRev = a.Revision < 0 ? 0 : a.Revision;
            int bRev = b.Revision < 0 ? 0 : b.Revision;
            return aRev.CompareTo(bRev);
        }

        /// <summary>
        /// Parses a string into a System.Version, stripping leading 'v', pre-release tags (-alpha, -rc1), etc.
        /// </summary>
        public static Version ParseCleanVersion(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString)) return null;

            versionString = versionString.Trim();
            var match = CleanVerRegex.Match(versionString);
            if (!match.Success) return null;

            var numericPart = match.Groups[1].Value;
            var segments = numericPart.Split('.');

            int major = 0, minor = 0, build = 0, rev = 0;
            if (segments.Length > 0) int.TryParse(segments[0], out major);
            if (segments.Length > 1) int.TryParse(segments[1], out minor);
            if (segments.Length > 2) int.TryParse(segments[2], out build);
            if (segments.Length > 3) int.TryParse(segments[3], out rev);

            return new Version(major, minor, build, rev);
        }
    }
}
