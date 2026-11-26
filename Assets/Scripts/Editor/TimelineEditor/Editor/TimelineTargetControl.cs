using System.Collections.Generic;
using System.Linq;

public class TimelineTargetControl
{
    private TimelineControl control;
    public TimelineControl Control => control;

    private Dictionary<string, List<UnitMono>> targets;
    public Dictionary<string, List<UnitMono>> Targets => targets;

    public TimelineTargetControl(TimelineControl control)
    {
        this.control = control;
        targets = new Dictionary<string, List<UnitMono>>();
    }

    public void AddTarget(string key, UnitMono unit)
    {
        if (!targets.ContainsKey(key))
        {
            targets.Add(key, new List<UnitMono>());
        }
        if (!targets[key].Contains(unit))
        {
            targets[key].Add(unit);
        }
    }

    public List<UnitMono> FindTargets(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return new List<UnitMono>();
        }
        if (!this.targets.ContainsKey(key))
        {
            return new List<UnitMono>();
        }
        return targets[key];
    }

    public List<UnitMono> FindTargets()
    {
        return control.PreviewBindings.Targets;
    }

    public UnitMono GetFirstTarget()
    {
        var selection = FindTargets();
        return selection.Count > 0 ? selection[0] : null;
    }

    public void ClearTarget()
    {
        targets.Clear();
    }

    public void ClearTargetKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (targets.TryGetValue(key, out var list))
        {
            list.Clear();
        }
    }

    public void Draw()
    {
    }

    public void OnStopPreView()
    {
        ClearTarget();
    }

    public void OnDestroy()
    {
        ClearTarget();
    }
}
