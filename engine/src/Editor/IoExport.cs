using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Gets a scene's I/O list out of the engine and into the two places a person
/// actually needs it: a mapping file the sidecar reads, and a table they can
/// work from while building the controller side.
///
/// Without this, connecting a scene you built yourself meant reading twenty
/// auto-generated tag ids off the inspector and retyping them into TIA Portal.
/// A typo there does not fail loudly — it produces an input that is false
/// forever, which looks exactly like a sensor that never triggers.
/// </summary>
public static class IoExport
{
    /// <summary>
    /// Mapping file for the OPC UA client driver: <c>{"tag.id": "ns=3;s=..."}</c>.
    ///
    /// Existing mappings are preserved and the rest are written empty, so this
    /// is safe to re-run after adding a part — you fill in the new blanks
    /// instead of redoing the lot. Keys beginning with an underscore are
    /// comments; the driver skips them, which is how a JSON file carries
    /// instructions to the person editing it.
    /// </summary>
    public static string WriteMappingFile(TagTable tags, string path,
                                          IReadOnlyDictionary<string, string>? existing = null)
    {
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"_comment\": \"FactoryForge I/O map: tag id -> OPC UA NodeId. " +
                        "Run 'python -m factoryforge_sidecar browse opc.tcp://<cpu-ip>:4840' " +
                        "and paste the exact ns=..;s=.. strings. The double quotes are part of " +
                        "the Siemens identifier and must stay escaped.\",");
        json.AppendLine("  \"_usage\": \"python -m factoryforge_sidecar demo --driver opcua-client " +
                        "--mapping <this file> -o url opc.tcp://<cpu-ip>:4840\",");

        var rows = new List<string>();
        foreach (var tag in tags)
        {
            string address = existing is not null && existing.TryGetValue(tag.Id, out var a) ? a : "";
            rows.Add($"  {Quote(tag.Id)}: {Quote(address)}");
        }
        json.AppendLine();
        json.AppendLine(string.Join(",\n", rows));
        json.AppendLine("}");

        return Write(path, json.ToString());
    }

    /// <summary>
    /// The I/O list as a spreadsheet: what each tag is, which direction it runs,
    /// and a suggested IEC address to build a symbol table from.
    /// </summary>
    public static string WriteTagCsv(TagTable tags, string path)
    {
        var suggested = SuggestIecAddresses(tags);

        var csv = new StringBuilder();
        csv.AppendLine("tag_id,description,type,direction,suggested_iec_address");
        foreach (var tag in tags)
        {
            // Input/Output are from the controller's point of view, so spell it
            // out rather than making the reader remember which way round it is.
            string direction = tag.Kind == TagKind.Input
                ? "PLC reads (sensor/button)"
                : "PLC writes (motor/valve/lamp)";
            csv.AppendLine(string.Join(",",
                Csv(tag.Id), Csv(tag.Name), Csv(tag.Type.ToString().ToLowerInvariant()),
                Csv(direction), Csv(suggested.GetValueOrDefault(tag.Id, ""))));
        }

        return Write(path, csv.ToString());
    }

    /// <summary>
    /// Suggested IEC addresses, allocated in tag order: bits get %I/%Q bit
    /// addresses, wider types get word addresses.
    ///
    /// A <em>suggestion</em>, deliberately. The OPC UA driver never uses it (it
    /// addresses by NodeId), and the Modbus driver derives its own layout in
    /// <c>modbus_tcp.py</c> — reimplementing that allocation here would create a
    /// second source of truth that silently drifts. This is for the human
    /// filling in a symbol table.
    /// </summary>
    public static Dictionary<string, string> SuggestIecAddresses(TagTable tags)
    {
        var map = new Dictionary<string, string>();
        int inBit = 0, outBit = 0, inWord = 0, outWord = 0;

        foreach (var tag in tags)
        {
            bool toPlc = tag.Kind == TagKind.Input;
            if (tag.Type == TagType.Bit)
            {
                ref int bit = ref (toPlc ? ref inBit : ref outBit);
                map[tag.Id] = $"%{(toPlc ? "I" : "Q")}{bit / 8}.{bit % 8}";
                bit++;
            }
            else
            {
                // Int and Float both take a doubleword, so the addresses stay
                // put if a tag's type is widened later.
                ref int word = ref (toPlc ? ref inWord : ref outWord);
                map[tag.Id] = $"%{(toPlc ? "I" : "Q")}D{word}";
                word += 4;
            }
        }

        return map;
    }

    private static string Write(string path, string contents)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PrintErr($"could not write {path}: {Godot.FileAccess.GetOpenError()}");
            return "";
        }
        file.StoreString(contents);
        return ProjectSettings.GlobalizePath(path);
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
