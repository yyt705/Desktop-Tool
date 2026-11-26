using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskFences
{
    internal sealed class DesktopMembership
    {
        private readonly Dictionary<string, long> _missingSince =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        private bool _seenPopulatedView;

        internal void ResetObservations()
        {
            _missingSince.Clear();
            _seenPopulatedView = false;
        }

        // Called with complete snapshots only. Explorer may briefly publish an empty
        // view as it starts. Never discard a layout on the first missing observation.
        internal bool Reconcile(List<FenceConfig> fences, List<DesktopIconInfo> icons, DesktopGridMetrics grid, long now)
        {
            if (icons.Count > 0) _seenPopulatedView = true;
            if (!_seenPopulatedView) return false;
            var present = icons.ToDictionary(ic => ic.Identity, StringComparer.OrdinalIgnoreCase);
            var claimed = new HashSet<string>(fences.SelectMany(f => f.MemberIconIds), StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (var fence in fences)
            {
                foreach (string name in fence.MemberIconNames.ToArray())
                {
                    if (icons.Count == 0) break;
                    var matches = icons.Where(ic => string.Equals(ic.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                    string missingKey = "legacy:" + fence.Id + ":" + name;
                    if (matches.Count == 0 && !MissingLongEnough(missingKey, now)) continue;
                    _missingSince.Remove(missingKey);
                    if (matches.Count > 1)
                        matches.RemoveAll(ic => !fence.IsInInnerRect(ic.GridCol(grid), ic.GridRow(grid)));
                    if (matches.Count == 1 && claimed.Add(matches[0].Identity))
                        fence.MemberIconIds.Add(matches[0].Identity);
                    fence.MemberIconNames.Remove(name);
                    changed = true;
                }

                foreach (string id in fence.MemberIconIds.ToArray())
                {
                    DesktopIconInfo item;
                    string path;
                    fence.MemberParsingNames.TryGetValue(id, out path);
                    if (present.TryGetValue(id, out item))
                    {
                        _missingSince.Remove(id);
                        if (!string.IsNullOrEmpty(item.ParsingName) && item.ParsingName != path)
                        {
                            fence.MemberParsingNames[id] = item.ParsingName;
                            changed = true;
                        }
                    }
                    else if ((icons.Count > 0 || !id.StartsWith("shell:", StringComparison.Ordinal)) &&
                             MissingLongEnough(id, now) && DesktopItemIdentity.ConfirmRemoval(id, path))
                    {
                        fence.MemberIconIds.Remove(id);
                        _missingSince.Remove(id);
                        changed = true;
                    }
                }
                foreach (string id in fence.MemberParsingNames.Keys.Except(fence.MemberIconIds, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    fence.MemberParsingNames.Remove(id);
                    changed = true;
                }
            }
            return changed;
        }

        private bool MissingLongEnough(string key, long now)
        {
            long since;
            if (_missingSince.TryGetValue(key, out since)) return now - since >= 3000;
            _missingSince[key] = now;
            return false;
        }
    }
}
