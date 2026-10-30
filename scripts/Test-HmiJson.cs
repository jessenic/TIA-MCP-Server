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

    static object InstanceInvoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
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
        NewDynamizationTests();
        object first = Document("HmiJsonItem");
        object second = Document("HmiJsonItem");
        Set(first, "Type", "HmiFaceplateContainer");
        Set(second, "Type", "HmiFaceplateContainer");
        Set(first, "ContainedType", "V1\\First");
        Set(second, "ContainedType", "V2\\Second");
        Check(!Equals(Invoke("GetItemIdentityKey", first), Invoke("GetItemIdentityKey", second)),
            "contained type separates default cache entries");
        var properties = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType("HmiJsonInterfaceProperty", BindingFlags.NonPublic)));
        object parameter = Document("HmiJsonInterfaceProperty");
        Set(parameter, "Name", "LevelPercent");
        Set(parameter, "Value", Invoke("CreateMappingValue", 60.25d));
        properties.Add(parameter);
        Invoke("ValidateInterfaceDocument", properties, 0);
        Check(true, "typed container interface accepted");
        properties.Add(parameter);
        Reject(() => Invoke("ValidateInterfaceDocument", properties, 0), "duplicate interface parameter");
        properties.RemoveAt(1);
        Reject(() => Invoke("ValidateInterfaceDocument", properties, 17), "interface nesting limit");
        Set(parameter, "Value", invalid);
        Reject(() => Invoke("ValidateInterfaceDocument", properties, 0), "unsupported interface value type");
        var inventory = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType("HmiJsonLibraryType", BindingFlags.NonPublic)));
        object type = Document("HmiJsonLibraryType");
        Set(type, "Name", "IndicatorLight");
        Set(type, "Guid", Guid.NewGuid());
        object version = Document("HmiJsonLibraryVersion");
        Set(version, "Version", "0.0.7");
        Set(version, "Guid", Guid.NewGuid());
        ((IList)Get(type, "Versions")).Add(version);
        inventory.Add(type);
        Check((bool)Invoke("SameLibraryInventory", inventory, inventory), "library GUID inventory matches itself");
        var empty = (IList)Activator.CreateInstance(inventory.GetType());
        Check(!(bool)Invoke("SameLibraryInventory", inventory, empty), "missing library definition rejected");
        if (args.Length > 2)
            Live(int.Parse(args[2]), args[3], args.Length > 4 && bool.Parse(args[4]));
        Console.WriteLine("PASS: " + checks + " HMI JSON regression checks.");
    }

    static void NewDynamizationTests()
    {
        var converter = Document("HmiJsonValueConverter");
        Set(converter, "ConditionType", "Bitmask");
        var entries = (IList)Get(converter, "Entries");
        var entry = Document("HmiJsonMappingEntry");
        Set(entry, "Type", "Bitmask");
        Set(entry, "BitCondition", ulong.MaxValue);
        Set(entry, "Relevant", 1UL << 63);
        Set(entry, "BitDynamizationType", "MultiBit");
        entries.Add(entry);
        Invoke("ValidateValueConverter", converter);
        Check(Json(entry).Contains("18446744073709551615") && Json(entry).Contains("9223372036854775808"),
            "64-bit bitmask values serialize without precision loss");
        Set(entry, "Relevant", null);
        Reject(() => Invoke("ValidateValueConverter", converter), "missing relevant mask");
        Set(entry, "Relevant", 1UL << 63);
        Set(entry, "BitDynamizationType", "InvalidMode");
        Reject(() => Invoke("ValidateValueConverter", converter), "invalid bitmask mode");
        Set(entry, "BitDynamizationType", "SingleBit");
        Invoke("ValidateValueConverter", converter);
        Check(true, "single-bit mapping accepted");

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var memberList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType("HmiJsonTagMember", BindingFlags.NonPublic)));
        var member = Document("HmiJsonTagMember");
        Set(member, "Name", "Fault");
        Set(member, "DataType", "Bool");
        memberList.Add(member);
        Invoke("AddTagMembers", tags, "Conveyor{0}", memberList, 0);
        Check((bool)Invoke("ContainsTagReference", tags, "Conveyor[0].Fault"), "structured member bracket lookup");
        Check(!(bool)Invoke("ContainsTagReference", tags, "Conveyor[0].Missing"), "unknown member rejected");
        Reject(() => Invoke("AddTagMembers", tags, "Conveyor", memberList, 33), "tag nesting limit");

        var script = Document("HmiJsonScriptDynamization");
        Set(script, "Code", "return 42;");
        Set(script, "GlobalDefinitionCode", "const value = 42;");
        Set(script, "TriggerType", "Tags");
        ((IList)Get(script, "Tags")).Add("Conveyor{0}.Fault");
        var dynamization = Document("HmiJsonDynamization");
        Set(dynamization, "Type", "Script");
        Set(dynamization, "Property", "Enabled");
        Set(dynamization, "Script", script);
        var buttonType = typeof(Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton);
        Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags);
        Check(true, "script trigger and code accepted");
        ((IList)Get(script, "Tags")).Add("Missing");
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "unknown trigger tag");
        ((IList)Get(script, "Tags")).Remove("Missing");
        Set(script, "TriggerType", "InvalidCycle");
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "invalid script cycle");
        Set(dynamization, "Script", null);
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "missing script configuration");

        Set(dynamization, "Type", "ResourceList");
        Set(dynamization, "Tag", "Conveyor{0}.Fault");
        Set(dynamization, "ResourceList", "StatusGraphics");
        Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags);
        Check(true, "resource-list binding accepted");
        Set(dynamization, "ResourceList", null);
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "missing resource-list name");
    }

    static void Live(int pid, string target, bool libraryTests)
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
            if (libraryTests) LibraryTests(manager, portal, target);
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
                    object sourceInterface = Invoke("CreateInterfaceDocument", source);
                    if (sourceInterface != null)
                    {
                        Invoke("ApplyInterfaceDocument", item, sourceInterface);
                        Check(Json(sourceInterface) == Json(Invoke("CreateInterfaceDocument", item)),
                            "faceplate instance interface read-back");
                    }
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
                foreach (var widget in allItems.OfType<HmiCustomWidgetContainer>())
                {
                    object expected = Invoke("CreateInterfaceDocument", widget);
                    Invoke("ApplyInterfaceDocument", widget, expected);
                    Check(Json(expected) == Json(Invoke("CreateInterfaceDocument", widget)), "SVG interface read-back");
                }
                // Deliberately never commit: every test object is rolled back.
            }
            Check(hmi.Screens.Count == screenCount && hmi.Screens.Find(name) == null, "temporary screen rolled back");
            Check(hmi.Tags.All(t => !t.Name.StartsWith("__JsonTag_", StringComparison.Ordinal)), "temporary tags rolled back");
        }
        finally { manager.Disconnect(); }
    }

    static void LibraryTests(TiaManager manager, TiaPortal portal, string target)
    {
        string directory = Path.Combine(Path.GetTempPath(), "tia-library-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exported = manager.ExportHmiJson(directory, false, false, target);
            Check((int)exported["libraryTypeCount"] > 0, "native library definitions exported");
            Check(File.Exists(Path.Combine(directory, (string)exported["libraryFile"])), "native archive exists");
            string screenPath = Directory.GetFiles(Path.Combine(directory, "screens")).FirstOrDefault();
            if (screenPath != null)
            {
                byte[] original = File.ReadAllBytes(screenPath);
                File.AppendAllText(screenPath, " ");
                string edited = File.ReadAllText(screenPath);
                bool rejected = false;
                try { manager.ExportHmiJson(directory, false, false, target); }
                catch (IOException) { rejected = true; }
                Check(rejected && File.ReadAllText(screenPath) == edited, "existing library archive rejected before overwriting JSON");
                File.WriteAllBytes(screenPath, original);
            }
            Check((bool)manager.ImportHmiJson(directory, false, true, target)["dryRun"], "native archive dry-run accepted");
            object plan = InstanceInvoke(manager, "ReadHmiJsonImportPlan", directory, true);
            object package = InstanceInvoke(manager, "OpenHmiLibrary", plan);
            string originalLibrary = Json(Invoke("DescribeLibraryTypes", manager.CurrentProject.ProjectLibrary.TypeFolder));
            using ((IDisposable)package)
            using (var access = portal.ExclusiveAccess("HMI library rollback-only regression"))
            using (var transaction = access.Transaction(manager.CurrentProject, "HMI library regression - rollback"))
            {
                var library = Get(package, "Library");
                var folder = Get(library, "TypeFolder");
                var types = (IEnumerable)Invoke("EnumerateLibraryTypes", folder);
                var missing = (IList)Get(package, "MissingTypes");
                Check(missing.Count == 0, "matching versions do not need replacement");
                var source = (Siemens.Engineering.Library.UserGlobalLibrary)library;
                source.UpdateLibrary(types.Cast<Siemens.Engineering.Library.Types.ILibraryTypeOrFolderSelection>(),
                    manager.CurrentProject.ProjectLibrary);
                InstanceInvoke(manager, "ApplyHmiLibrary", package);
                Check(true, "native library copy is supported inside project rollback transaction");
            }
            Check(originalLibrary == Json(Invoke("DescribeLibraryTypes", manager.CurrentProject.ProjectLibrary.TypeFolder)),
                "project type/version inventory unchanged after rollback");
            string archive = Path.Combine(directory, (string)exported["libraryFile"]);
            File.AppendAllText(archive, "tampered");
            Reject(() => InstanceInvoke(manager, "ReadHmiJsonImportPlan", directory, true), "tampered native archive hash");
            object manifest = Get(plan, "Manifest");
            Set(manifest, "LibraryFile", "..\\escape.zal21");
            Reject(() => InstanceInvoke(manager, "OpenHmiLibrary", plan), "library archive traversal");
        }
        finally { Directory.Delete(directory, true); }
    }
}
