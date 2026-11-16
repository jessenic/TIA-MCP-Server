using System.Collections.Generic;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public HmiScreenGroupsDiscovery ListHmiScreenGroups(string? targetName = null)
        {
            HmiSoftware hmi = FindHmiSoftware(targetName);
            var result = new HmiScreenGroupsDiscovery
            {
                Scope = "Screen-group hierarchy and direct screen counts for the selected HMI Unified software.",
                Completeness = "Complete for the public V21 ScreenGroups composition; screen and item details are not included."
            };

            AddHmiScreenGroups(hmi.ScreenGroups, result.Groups);
            return result;
        }

        public HmiGraphicListsDiscovery ListHmiGraphicLists(string? targetName = null)
        {
            HmiSoftware hmi = FindHmiSoftware(targetName);
            var result = new HmiGraphicListsDiscovery
            {
                Scope = "Graphic-list names for the selected HMI Unified software.",
                Completeness = "Complete for the public V21 HmiGraphicLists composition; graphic-list entries and file-backed assets are not exposed."
            };

            foreach (var graphicList in hmi.HmiGraphicLists)
            {
                result.GraphicLists.Add(new HmiGraphicListInfo { Name = graphicList.Name });
            }

            return result;
        }

        private static void AddHmiScreenGroups(
            HmiScreenGroupComposition groups,
            List<HmiScreenGroupInfo> result)
        {
            foreach (HmiScreenGroup group in groups)
            {
                var groupInfo = new HmiScreenGroupInfo
                {
                    Name = group.Name,
                    DirectScreenCount = group.Screens.Count
                };
                AddHmiScreenGroups(group.Groups, groupInfo.ChildGroups);
                result.Add(groupInfo);
            }
        }
    }

    public sealed class HmiScreenGroupsDiscovery
    {
        public string Scope { get; set; } = "";
        public string Completeness { get; set; } = "";
        public List<HmiScreenGroupInfo> Groups { get; } = new List<HmiScreenGroupInfo>();
    }

    public sealed class HmiScreenGroupInfo
    {
        public string Name { get; set; } = "";
        public int DirectScreenCount { get; set; }
        public List<HmiScreenGroupInfo> ChildGroups { get; } = new List<HmiScreenGroupInfo>();
    }

    public sealed class HmiGraphicListsDiscovery
    {
        public string Scope { get; set; } = "";
        public string Completeness { get; set; } = "";
        public List<HmiGraphicListInfo> GraphicLists { get; } = new List<HmiGraphicListInfo>();
    }

    public sealed class HmiGraphicListInfo
    {
        public string Name { get; set; } = "";
    }
}
