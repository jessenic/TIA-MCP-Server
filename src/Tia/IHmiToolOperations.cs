using System.Text.Json;

namespace TiaOpennessMcp.Tia
{
    public interface IHmiToolOperations
    {
        object GetHmiScreenItem(string screenName, string itemName, string? targetName = null);

        object UpdateHmiScreenItemProperties(
            string screenName,
            string itemName,
            JsonElement properties,
            string? targetName = null);

        HmiEventScriptsDetails GetHmiEventScripts(
            string screenName,
            string? itemName = null,
            string? targetName = null);

        HmiEventScriptUpdateResult UpdateHmiEventScript(
            string screenName,
            string eventType,
            string scriptCode,
            string? itemName = null,
            string? targetName = null);

        object SetExpressionDynamization(
            string screenName,
            string itemName,
            string propertyName,
            string expression,
            string resultType,
            string onConflict = "fail",
            string? targetName = null);

        object SetExpressionDynamizationEntries(
            string screenName,
            string itemName,
            string propertyName,
            JsonElement entries,
            string onConflict = "fail",
            string? targetName = null);

        object DeleteHmiDynamization(
            string screenName,
            string itemName,
            string propertyName,
            string? targetName = null);
    }
}
