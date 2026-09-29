using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.Census;

/// <summary>
/// Dumps one level's PlayMaker FSMs, global variables and rigidbodies as plain, diffable text.
/// Read-only by design: actions that are not loaded yet are read from their serialized data via
/// reflection instead of being deserialized, so taking a census never changes the game's state.
/// </summary>
internal sealed class CensusWriter
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ActionNamespace = "HutongGames.PlayMaker.Actions.";
    private const int MaxValueLength = 160;
    private const int MaxParamsPerAction = 16;

    // ActionData keeps each state's serialized actions in parallel private lists.
    private static readonly FieldInfo? ActionNames = typeof(ActionData).GetField("actionNames", Hidden);
    private static readonly FieldInfo? ActionStarts = typeof(ActionData).GetField("actionStartIndex", Hidden);
    private static readonly FieldInfo? ParamNames = typeof(ActionData).GetField("paramName", Hidden);
    private static readonly FieldInfo? ParamTypes = typeof(ActionData).GetField("paramDataType", Hidden);
    private static readonly FieldInfo? ParamPositions = typeof(ActionData).GetField("paramDataPos", Hidden);
    private static readonly FieldInfo? ParamSizes = typeof(ActionData).GetField("paramByteDataSize", Hidden);
    private static readonly FieldInfo? StringParams = typeof(ActionData).GetField("fsmStringParams", Hidden);
    private static readonly FieldInfo? ByteData = typeof(ActionData).GetField("byteData", Hidden);

    private readonly string _folder;
    private readonly Dictionary<string, int> _actionCounts = new Dictionary<string, int>();
    private readonly List<string> _saveActions = new List<string>();
    private readonly List<string> _levelActions = new List<string>();

    public CensusWriter(string folder)
    {
        _folder = folder;
    }

    public string WriteAll()
    {
        var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)).Cast<PlayMakerFSM>()
            .Where(f => f != null && f.gameObject != null)
            .Select(f => new { Fsm = f, Path = PathOf(f.gameObject) })
            .OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Fsm.FsmName, StringComparer.Ordinal)
            .ToList();
        var live = new HashSet<PlayMakerFSM>(PlayMakerFSM.FsmList);
        var states = 0;
        var actions = 0;
        using (var w = Open("fsms.txt"))
        {
            foreach (var entry in fsms) WriteFsm(w, entry.Fsm, entry.Path, live.Contains(entry.Fsm), ref states, ref actions);
        }

        var globals = WriteGlobals();
        var bodies = WriteRigidbodies();
        WriteSummary(fsms.Count, live.Count, states, actions, globals, bodies);
        return fsms.Count + " FSMs (" + live.Count + " live), " + states + " states, " + actions + " actions, " + globals + " globals, " + bodies + " rigidbodies";
    }

    private void WriteFsm(StreamWriter w, PlayMakerFSM component, string path, bool isLive, ref int stateCount, ref int actionCount)
    {
        Fsm fsm;
        try
        {
            fsm = component.Fsm;
        }
        catch (Exception e)
        {
            w.WriteLine("FSM " + path + " :: " + component.FsmName + " (unreadable: " + e.GetType().Name + ")");
            return;
        }

        var where = path + " :: " + component.FsmName;
        w.WriteLine("FSM " + where + " [" + (isLive ? "live" : "dormant") + ", " + (component.gameObject.activeInHierarchy ? "active" : "inactive")
            + "] state=" + (fsm.ActiveStateName ?? "-") + (component.UsesTemplate ? " template=" + component.FsmTemplate.name : string.Empty));

        foreach (var variable in SafeVariables(fsm.Variables)) w.WriteLine("  VAR " + variable);
        foreach (var fsmEvent in fsm.Events ?? new FsmEvent[0]) w.WriteLine("  EVENT " + fsmEvent.Name + (fsmEvent.IsGlobal ? " (global)" : string.Empty));
        foreach (var transition in fsm.GlobalTransitions ?? new FsmTransition[0]) w.WriteLine("  GLOBAL " + transition.EventName + " -> " + transition.ToState);

        foreach (var state in fsm.States ?? new FsmState[0])
        {
            stateCount++;
            w.WriteLine("  STATE " + state.Name);
            foreach (var line in DescribeActions(state))
            {
                actionCount++;
                w.WriteLine("    ACTION " + line);
                var typeName = line.Split(' ')[0];
                Bump(typeName);
                if (IsSaveRelated(typeName)) _saveActions.Add(where + " / " + state.Name + " : " + line);
                if (typeName.StartsWith("LoadLevel", StringComparison.Ordinal) || typeName == "RestartLevel") _levelActions.Add(where + " / " + state.Name + " : " + line);
            }

            foreach (var transition in state.Transitions ?? new FsmTransition[0]) w.WriteLine("    ON " + transition.EventName + " -> " + transition.ToState);
        }
    }

    /// <summary>Loaded actions are read directly; unloaded ones from serialized data, never deserialized.</summary>
    private IEnumerable<string> DescribeActions(FsmState state)
    {
        if (state.ActionsLoaded)
        {
            foreach (var action in state.Actions)
            {
                if (action == null) continue;
                var parts = new List<string>();
                foreach (var field in ActionData.GetFields(action.GetType()))
                {
                    if (parts.Count >= MaxParamsPerAction) break;
                    string? value;
                    try
                    {
                        value = Format(field.GetValue(action));
                    }
                    catch (Exception)
                    {
                        value = null;
                    }

                    if (value != null) parts.Add(field.Name + "=" + value);
                }

                yield return Short(action.GetType().FullName ?? action.GetType().Name) + (parts.Count > 0 ? " " + string.Join(" ", parts.ToArray()) : string.Empty);
            }

            yield break;
        }

        foreach (var line in DescribeSerialized(state.ActionData)) yield return line;
    }

    private static IEnumerable<string> DescribeSerialized(ActionData data)
    {
        var names = ActionNames?.GetValue(data) as List<string>;
        if (names == null) yield break;
        var starts = ActionStarts?.GetValue(data) as List<int>;
        var paramNames = ParamNames?.GetValue(data) as List<string>;
        var paramTypes = ParamTypes?.GetValue(data) as IList;
        var positions = ParamPositions?.GetValue(data) as List<int>;
        var sizes = ParamSizes?.GetValue(data) as List<int>;
        var strings = StringParams?.GetValue(data) as List<FsmString>;
        var bytes = ByteData?.GetValue(data) as List<byte>;

        for (var i = 0; i < names.Count; i++)
        {
            var parts = new List<string>();
            if (starts != null && paramNames != null && paramTypes != null && positions != null && i < starts.Count)
            {
                var end = i + 1 < starts.Count ? starts[i + 1] : paramNames.Count;
                for (var p = starts[i]; p < end && p < paramNames.Count && parts.Count < MaxParamsPerAction; p++)
                {
                    var kind = paramTypes[p]?.ToString();
                    string? value = null;
                    if (kind == "FsmString" && strings != null && positions[p] < strings.Count) value = Format(strings[positions[p]]);
                    else if (kind == "String" && bytes != null && sizes != null) value = Quote(DecodeString(bytes, positions[p], sizes[p]));
                    if (value != null) parts.Add(paramNames[p] + "=" + value);
                }
            }

            yield return Short(names[i]) + (parts.Count > 0 ? " " + string.Join(" ", parts.ToArray()) : string.Empty) + " (serialized)";
        }
    }

    private int WriteGlobals()
    {
        var count = 0;
        using (var w = Open("globals.txt"))
        {
            foreach (var variable in SafeVariables(FsmVariables.GlobalVariables))
            {
                w.WriteLine(variable);
                count++;
            }

            w.WriteLine();
            foreach (var name in FsmEvent.globalEvents ?? new List<string>()) w.WriteLine("EVENT " + name);
        }

        return count;
    }

    private int WriteRigidbodies()
    {
        var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody)).Cast<Rigidbody>().Where(b => b != null && b.gameObject != null)
            .Select(b => new { Body = b, Path = PathOf(b.gameObject) }).OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
        using (var w = Open("rigidbodies.txt"))
        {
            w.WriteLine("path\tactive\tkinematic\tsleeping\tmass\tposition\tlayer\ttag\tfsms");
            foreach (var entry in bodies)
            {
                var b = entry.Body;
                var fsmNames = b.GetComponents<PlayMakerFSM>().Select(f => f.FsmName).ToArray();
                w.WriteLine(entry.Path + "\t" + b.gameObject.activeInHierarchy + "\t" + b.isKinematic + "\t" + b.IsSleeping() + "\t" + b.mass.ToString("0.###")
                    + "\t" + b.position.ToString("F2") + "\t" + LayerMask.LayerToName(b.gameObject.layer) + "\t" + b.tag + "\t" + string.Join(",", fsmNames));
            }
        }

        return bodies.Count;
    }

    private void WriteSummary(int fsmCount, int liveCount, int states, int actions, int globals, int bodies)
    {
        using (var w = Open("summary.txt"))
        {
            w.WriteLine("Level " + Application.loadedLevel + " (" + Application.loadedLevelName + "), Unity " + Application.unityVersion + ", taken " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            w.WriteLine(fsmCount + " FSMs (" + liveCount + " live), " + states + " states, " + actions + " actions, " + globals + " globals, " + bodies + " rigidbodies");
            w.WriteLine();
            w.WriteLine("== Level loads (" + _levelActions.Count + ")");
            foreach (var line in _levelActions) w.WriteLine(line);
            w.WriteLine();
            w.WriteLine("== Save and load related actions (" + _saveActions.Count + ")");
            foreach (var line in _saveActions) w.WriteLine(line);
            w.WriteLine();
            w.WriteLine("== Action types by use");
            foreach (var pair in _actionCounts.OrderByDescending(p => p.Value)) w.WriteLine(pair.Value + "\t" + pair.Key);
            w.WriteLine();
            w.WriteLine("== Root objects");
            var roots = Resources.FindObjectsOfTypeAll(typeof(Transform)).Cast<Transform>()
                .Where(t => t != null && t.parent == null && t.gameObject.activeInHierarchy).Select(t => t.name).OrderBy(n => n, StringComparer.Ordinal);
            foreach (var name in roots) w.WriteLine(name);
        }
    }

    private static IEnumerable<string> SafeVariables(FsmVariables? variables)
    {
        if (variables == null) yield break;
        NamedVariable[] all;
        try
        {
            all = variables.GetAllNamedVariables();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var variable in all.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            string value;
            try
            {
                value = Clip(variable.ToString());
            }
            catch (Exception)
            {
                value = "?";
            }

            yield return variable.GetType().Name + " " + variable.Name + " = " + value;
        }
    }

    private static string? Format(object? value)
    {
        switch (value)
        {
            case null: return null;
            case FsmString s: return s.UseVariable ? "{" + s.Name + "}" : Quote(s.Value);
            case FsmInt i: return i.UseVariable ? "{" + i.Name + "}" : i.Value.ToString();
            case FsmFloat f: return f.UseVariable ? "{" + f.Name + "}" : f.Value.ToString("0.###");
            case FsmBool b: return b.UseVariable ? "{" + b.Name + "}" : b.Value.ToString();
            case FsmEvent e: return "event:" + e.Name;
            case FsmGameObject g: return g.UseVariable ? "{" + g.Name + "}" : g.Value != null ? "go:" + g.Value.name : null;
            case FsmOwnerDefault o: return o.OwnerOption == OwnerDefaultOption.UseOwner ? "owner" : Format(o.GameObject);
            case string text: return Quote(text);
            case bool or int or float or Enum: return value.ToString();
            case FsmString[] array: return "[" + string.Join(", ", array.Select(item => Format(item) ?? "-").ToArray()) + "]";
            default: return null;
        }
    }

    private static string DecodeString(List<byte> bytes, int start, int length)
    {
        if (start < 0 || length <= 0 || start + length > bytes.Count) return string.Empty;
        return Encoding.UTF8.GetString(bytes.GetRange(start, length).ToArray());
    }

    private static string Quote(string? text) => "\"" + Clip((text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")) + "\"";

    private static string Clip(string text) => text.Length <= MaxValueLength ? text : text.Substring(0, MaxValueLength) + "...";

    private static string Short(string typeName) =>
        typeName.StartsWith(ActionNamespace, StringComparison.Ordinal) ? typeName.Substring(ActionNamespace.Length) : typeName;

    private static bool IsSaveRelated(string typeName) =>
        typeName.IndexOf("ES2", StringComparison.Ordinal) >= 0 || typeName.IndexOf("Save", StringComparison.Ordinal) >= 0
        || typeName.IndexOf("PlayerPrefs", StringComparison.Ordinal) >= 0 || typeName.IndexOf("Load", StringComparison.Ordinal) >= 0;

    private void Bump(string typeName)
    {
        int count;
        _actionCounts.TryGetValue(typeName, out count);
        _actionCounts[typeName] = count + 1;
    }

    private StreamWriter Open(string name) => new StreamWriter(Path.Combine(_folder, name), false, new UTF8Encoding(false), 1 << 16);

    private static string PathOf(GameObject go)
    {
        var parts = new List<string>();
        for (var t = go.transform; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }
}
