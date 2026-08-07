using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace FactoryForge.TagBus;

/// <summary>
/// The authoritative tag collection. Mirrors TagTable in
/// sidecar/factoryforge_sidecar/tags.py.
/// </summary>
public sealed class TagTable : IEnumerable<Tag>
{
    private readonly Dictionary<string, Tag> _tags = new();
    private readonly Dictionary<string, object> _forced = new();

    public int Count => _tags.Count;

    public void Add(Tag tag)
    {
        if (_tags.ContainsKey(tag.Id))
            throw new System.ArgumentException($"duplicate tag id {tag.Id}");
        _tags[tag.Id] = tag;
    }

    public bool Contains(string id) => _tags.ContainsKey(id);

    public Tag? Get(string id) => _tags.TryGetValue(id, out var t) ? t : null;

    public Tag this[string id] => _tags[id];

    public IEnumerable<Tag> ByKind(TagKind kind) => _tags.Values.Where(t => t.Kind == kind);

    /// <summary>
    /// Set a tag's value. Returns true if it actually changed.
    /// A forced tag absorbs the write silently — the underlying value updates so
    /// that clearing the force reveals something sensible, but the observable
    /// value stays pinned and no change is reported.
    /// </summary>
    public bool Set(string id, object value)
    {
        var tag = _tags[id];
        bool changed = tag.Differs(value);
        tag.Set(value);
        return !_forced.ContainsKey(id) && changed;
    }

    public bool Force(string id, object value)
    {
        var tag = _tags[id];
        var coerced = tag.Coerce(value);
        var wasVisible = Visible(id);
        _forced[id] = coerced;
        return !Equals(coerced, wasVisible);
    }

    public bool ClearForce(string id)
    {
        if (!_forced.Remove(id, out var pinned)) return false;
        return _tags[id].Differs(pinned);
    }

    public bool IsForced(string id) => _forced.ContainsKey(id);

    /// <summary>The value the outside world sees, honouring any force.</summary>
    public object Visible(string id) =>
        _forced.TryGetValue(id, out var v) ? v : _tags[id].Value;

    public JsonArray ToJson()
    {
        var arr = new JsonArray();
        foreach (var tag in _tags.Values)
            arr.Add(tag.ToJson(Visible(tag.Id), IsForced(tag.Id)));
        return arr;
    }

    public IEnumerator<Tag> GetEnumerator() => _tags.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
