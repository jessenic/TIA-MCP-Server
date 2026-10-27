using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Screens;
using TiaOpennessMcp.Tia;

class HmiJsonTests
{
    static string binaryDirectory;
    static string apiDirectory;
    static int checks;

    static int Main(string[] args)
    {
        binaryDirectory = args[0];
        apiDirectory = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        foreach (string directory in new[] { binaryDirectory, apiDirectory })
            foreach (string extension in new[] { ".dll", ".exe" })
            {
                string path = Path.Combine(directory, name + extension);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
        return null;
    }

    static object Invoke(string name, params object[] args)
    {
        try
        {
            return typeof(TiaManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, args);
        }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    static object Document(string name)
    {
        return Activator.CreateInstance(typeof(TiaManager).GetNestedType(name, BindingFlags.NonPublic), true);
    }

    static void Set(object target, string name, object value)
    {
        target.GetType().GetProperty(name).SetValue(target, value, null);
    }

    static object Get(object target, string name)
    {
        return target.GetType().GetProperty(name).GetValue(target, null);
    }

    static string Json(object value)
    {
        return JsonSerializer.Serialize(value, value.GetType(), new JsonSerializerOptions());
    }

    static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL: " + message);
        checks++;
    }

    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception("FAIL: expected invalid input rejection: " + message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string[] args)
    {
        object[] variants = {
            "raw <ProjectText>markup</ProjectText>", true, false, (byte)255,
            (short)-32000, (ushort)65000, -123, uint.MaxValue, long.MinValue,
            ulong.MaxValue, 1.25f, 1.25d, 1.25m, Color.FromArgb(128, 10, 20, 30)
        };
        foreach (object value in variants)
        {
            object encoded = Invoke("CreateMappingValue", value);
            object decoded = Invoke("ReadMappingValue", encoded);
            Check(value.GetType() == decoded.GetType() && value.Equals(decoded),
                "mapping value round-trip: " + value.GetType().Name);
        }
        object invalid = Document("HmiJsonMappingValue");
        Set(invalid, "Type", "ArbitraryObject");
        Reject(() => Invoke("ReadMappingValue", invalid), "unknown value type");
        object expression = Document("HmiJsonDynamization");
        Set(expression, "Type", "Expression");
        Set(expression, "Property", "Enabled");
        Reject(() => Invoke("ValidateDynamizationDocument", typeof(Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton),
            expression, new Dictionary<string, string>()), "legacy expression placeholder");
        var arrayTags = new Dictionary<string, string> { { "Values", "Array [0..2] of Real" } };
        Check((bool)Invoke("ContainsTagReference", arrayTags, "Values[0]"), "array element reference");
        Check(!(bool)Invoke("ContainsTagReference", arrayTags, "Values[3]"), "out-of-range array reference rejected");
        object first = Document("HmiJsonItem");
        object second = Document("HmiJsonItem");
        Set(first, "Type", "HmiFaceplateContainer");
        Set(second, "Type", "HmiFaceplateContainer");
        Set(first, "ContainedType", "V1\\First");
        Set(second, "ContainedType", "V2\\Second");
        Check(!Equals(Invoke("GetItemIdentityKey", first), Invoke("GetItemIdentityKey", second)),
            "contained type separates default cache entries");
        if (args.Length > 2)
            Live(int.Parse(args[2]), args[3]);
        Console.WriteLine("PASS: " + checks + " HMI JSON regression checks.");
    }

    static void Live(int pid, string target)
    {
        var manager = TiaManager.Instance;
        var connected = manager.Connect(pid);
        Check(connected.Success && connected.ProcessId == pid, "exact project connection");
        var hmi = manager.FindHmiSoftware(target);
        var screens = ((IEnumerable)Invoke("EnumerateHmiScreens", hmi.Screens, hmi.ScreenGroups)).Cast<HmiScreen>().ToList();
        int screenCount = hmi.Screens.Count;
        string name = "__JsonRegression_" + Guid.NewGuid().ToString("N");
        var portal = (TiaPortal)typeof(TiaManager).GetField("_tiaPortal", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(manager);
        var allItems = screens.SelectMany(s => s.ScreenItems).ToList();
        try
        {
            using (var access = portal.ExclusiveAccess("HMI JSON rollback-only regression tests"))
            using (var transaction = access.Transaction(manager.CurrentProject, "HMI JSON regression tests - rollback"))
            {
                HmiScreen temporary = hmi.Screens.Create(name);
                int index = 0;
                foreach (var source in allItems)
                {
                    if (source is HmiCustomWidgetContainer) continue;
                    string contained = (source as HmiContainerBase) == null ? null : ((HmiContainerBase)source).ContainedType;
                    var item = (HmiScreenItemBase)Invoke("CreateScreenItem", temporary, "Item_" + index++,
                        source.GetType(), contained);
                    Check(item.GetType() == source.GetType(), "create " + source.GetType().Name);
                    var documents = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
                        typeof(TiaManager).GetNestedType("HmiJsonDynamization", BindingFlags.NonPublic)));
                    foreach (var dynamization in source.Dynamizations)
                        documents.Add(Invoke("CreateDynamizationDocument", dynamization));
                    Invoke("ApplyDynamizations", hmi, item, documents, true);
                    Check(item.Dynamizations.Count == documents.Count, "dynamization count");
                    foreach (object expected in documents)
                    {
                        object actual = Invoke("CreateDynamizationDocument",
                            item.Dynamizations.Find((string)Get(expected, "Property")));
                        Check(Json(expected) == Json(actual), "converter and typed mapping read-back for " +
                            source.Name + "." + Get(expected, "Property") + "\nExpected: " + Json(expected) + "\nActual: " + Json(actual));
                    }
                    var events = Invoke("ReadItemEvents", source);
                    Invoke("ApplyItemEvents", item, events);
                    Check(Json(events) == Json(Invoke("ReadItemEvents", item)), "event script read-back");
                    if (documents.Count > 1)
                    {
                        var single = (IList)Activator.CreateInstance(documents.GetType());
                        single.Add(documents[0]);
                        Invoke("ApplyDynamizations", hmi, item, single, false);
                        Check(item.Dynamizations.Count == documents.Count, "patch binding preserves unrelated bindings");
                    }
                    foreach (object document in documents)
                    {
                        object converter = Get(document, "ValueConverter");
                        if (converter == null || String.IsNullOrEmpty((string)Get(converter, "Formula"))) continue;
                        Set(converter, "Formula", "");
                        Set(converter, "IsFormulaSelected", false);
                        var single = (IList)Activator.CreateInstance(documents.GetType());
                        single.Add(document);
                        Invoke("ApplyDynamizations", hmi, item, single, false);
                        object actual = Invoke("CreateDynamizationDocument",
                            item.Dynamizations.Find((string)Get(document, "Property")));
                        Check(Json(document) == Json(actual), "clearing an existing converter formula");
                    }
                }
                foreach (var tag in hmi.Tags.ToList().Where(t => String.IsNullOrEmpty(t.PlcTag)))
                {
                    object document = Invoke("CreateTagDocument", tag);
                    string tagName = "__JsonTag_" + Guid.NewGuid().ToString("N");
                    Set(document, "Name", tagName);
                    Invoke("ApplyTagDocument", hmi, document);
                    var actual = hmi.Tags.Find(tagName);
                    Check(actual.DataType == tag.DataType, "internal tag data type");
                    Check(Json(Invoke("CreateLimitDocument", actual.InitialMinValue) ?? new object()) ==
                        Json(Invoke("CreateLimitDocument", tag.InitialMinValue) ?? new object()), "unset tag limit");
                }
                // Deliberately never commit: every test object is rolled back.
            }
            Check(hmi.Screens.Count == screenCount && hmi.Screens.Find(name) == null, "temporary screen rolled back");
            Check(hmi.Tags.All(t => !t.Name.StartsWith("__JsonTag_", StringComparison.Ordinal)), "temporary tags rolled back");
        }
        finally { manager.Disconnect(); }
    }
}
